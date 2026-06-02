using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AutoToolCatalog.Services.Seco;

/// <summary>
/// Resolves SECO item numbers and loads product JSON from a real browser when HttpClient is blocked.
/// </summary>
internal static partial class SecoBrowserApiFetcher
{
    private const string BaseUrl = "https://www.secotools.com";
    private const int NavigationTimeoutMs = 120_000;
    private static readonly SemaphoreSlim BrowserGate = new(1, 1);
    private static bool _playwrightReady;

    public sealed record BrowserFetchResult(string ItemNumber, string ProductUrl, string Json);

    public static async Task<BrowserFetchResult?> FetchAsync(
        string? webpageLink,
        string designation,
        string? knownItemNumber,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(designation) &&
            string.IsNullOrWhiteSpace(webpageLink) &&
            string.IsNullOrWhiteSpace(knownItemNumber))
            return null;

        await BrowserGate.WaitAsync(ct);
        try
        {
            await EnsurePlaywrightAsync(ct);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();

            var itemNumber = knownItemNumber ?? ExtractItemNumberFromUrl(webpageLink);

            string? json;
            string? productUrl;
            if (!string.IsNullOrWhiteSpace(itemNumber))
            {
                productUrl = $"{BaseUrl}/article/p_{itemNumber}";
                json = await LoadProductJsonFromPageAsync(page, productUrl, itemNumber, ct);
            }
            else if (!string.IsNullOrWhiteSpace(designation))
            {
                (itemNumber, productUrl, json) = await SearchAndLoadViaUiAsync(page, designation.Trim(), ct);
            }
            else
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(itemNumber) || string.IsNullOrWhiteSpace(json))
                return null;

            productUrl ??= $"{BaseUrl}/article/p_{itemNumber}";
            return new BrowserFetchResult(itemNumber, productUrl, json);
        }
        finally
        {
            BrowserGate.Release();
        }
    }

    private static async Task WarmupAsync(IPage page, CancellationToken ct)
    {
        await page.GotoAsync(BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = NavigationTimeoutMs
        });
        ct.ThrowIfCancellationRequested();
        await TryDismissCookiesAsync(page);
    }

    private static async Task<string?> LoadProductJsonFromPageAsync(
        IPage page, string productUrl, string itemNumber, CancellationToken ct)
    {
        try
        {
            var responseTask = page.WaitForResponseAsync(
                r => r.Url.Contains("GetFullProduct", StringComparison.OrdinalIgnoreCase) && r.Ok,
                new PageWaitForResponseOptions { Timeout = NavigationTimeoutMs });

            await page.GotoAsync(productUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = NavigationTimeoutMs
            });

            var response = await responseTask;
            ct.ThrowIfCancellationRequested();
            var json = await response.TextAsync();
            if (!string.IsNullOrWhiteSpace(json) && json.Contains("Attributes", StringComparison.OrdinalIgnoreCase))
                return json;
        }
        catch
        {
            // fall through to manual fetch / HTML extraction
        }

        ct.ThrowIfCancellationRequested();
        if (!page.Url.Contains(itemNumber, StringComparison.Ordinal))
        {
            await page.GotoAsync(productUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = NavigationTimeoutMs
            });
        }

        var fetched = await FetchGetFullProductAsync(page, itemNumber);
        if (!string.IsNullOrWhiteSpace(fetched))
            return fetched;

        var html = await page.ContentAsync();
        return ExtractProductJsonFromHtml(html);
    }

    private static async Task<(string? ItemNumber, string? ProductUrl, string? Json)> SearchAndLoadViaUiAsync(
        IPage page, string designation, CancellationToken ct)
    {
        await WarmupAsync(page, ct);

        ILocator? searchInput = null;
        foreach (var selector in new[] { "input[type='search']", "input[name='search']", "#search" })
        {
            var inputs = page.Locator(selector);
            if (await inputs.CountAsync() > 0)
            {
                searchInput = inputs.First;
                break;
            }
        }

        if (searchInput == null)
        {
            try
            {
                searchInput = page.GetByRole(AriaRole.Searchbox).First;
                await searchInput.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = 30_000
                });
            }
            catch
            {
                return (null, null, null);
            }
        }

        await searchInput.FillAsync(designation);
        await searchInput.PressAsync("Enter");

        await page.WaitForTimeoutAsync(3000);
        ct.ThrowIfCancellationRequested();

        var itemNumber = await TryGetItemNumberFromResultLinksAsync(page);
        var html = await page.ContentAsync();
        if (string.IsNullOrWhiteSpace(itemNumber))
            itemNumber = PickItemNumberFromHtml(html, designation);
        if (string.IsNullOrWhiteSpace(itemNumber))
        {
            var first = ArticlePathRegex().Match(html);
            if (first.Success)
                itemNumber = first.Groups[1].Value;
        }

        if (string.IsNullOrWhiteSpace(itemNumber))
            return (null, null, null);

        var productUrl = $"{BaseUrl}/article/p_{itemNumber}";
        var json = await LoadProductJsonFromPageAsync(page, productUrl, itemNumber, ct);
        return (itemNumber, productUrl, json);
    }

    private static async Task<string?> TryGetItemNumberFromResultLinksAsync(IPage page)
    {
        try
        {
            var links = page.Locator("a[href*='article/p_']");
            await links.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = NavigationTimeoutMs
            });
            var href = await links.First.GetAttributeAsync("href");
            return ExtractItemNumberFromUrl(href);
        }
        catch
        {
            return null;
        }
    }

    private static async Task TryDismissCookiesAsync(IPage page)
    {
        foreach (var selector in new[]
        {
            "#onetrust-accept-btn-handler",
            "button:has-text('Accept all')",
            "button:has-text('Accept All')",
            "button:has-text('Accept')"
        })
        {
            try
            {
                await page.Locator(selector).First.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
                await page.WaitForTimeoutAsync(500);
                return;
            }
            catch
            {
                // try next selector
            }
        }
    }

    private static async Task<string?> FetchGetFullProductAsync(IPage page, string itemNumber) =>
        await page.EvaluateAsync<string?>(@"
            async (itemNumber) => {
                const url = 'https://www.secotools.com/core/api/Products/GetFullProduct?itemNumber=' + encodeURIComponent(itemNumber);
                const response = await fetch(url, {
                    headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
                });
                if (!response.ok) return null;
                return await response.text();
            }
        ", itemNumber);

    private static string? ExtractProductJsonFromHtml(string html)
    {
        var match = ProductJsonRegex().Match(html);
        if (!match.Success)
            return null;

        var json = match.Groups[0].Value;
        return json.Contains("Attributes", StringComparison.OrdinalIgnoreCase) ? json : null;
    }

    private static string? PickItemNumberFromHtml(string html, string designation)
    {
        var designationUpper = designation.ToUpperInvariant();

        foreach (Match match in ArticlePathRegex().Matches(html))
        {
            var start = Math.Max(0, match.Index - 400);
            var length = Math.Min(html.Length - start, 800);
            var window = html.AsSpan(start, length).ToString().ToUpperInvariant();
            if (window.Contains(designationUpper, StringComparison.Ordinal))
                return match.Groups[1].Value;
        }

        var itemMatch = ItemNumberJsonRegex().Match(html);
        if (itemMatch.Success)
            return itemMatch.Groups[1].Value;

        var firstArticle = ArticlePathRegex().Match(html);
        if (firstArticle.Success)
            return firstArticle.Groups[1].Value;

        return null;
    }

    private static string? ExtractItemNumberFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var match = ArticlePathRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task EnsurePlaywrightAsync(CancellationToken ct)
    {
        if (_playwrightReady) return;
        ct.ThrowIfCancellationRequested();
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
            throw new InvalidOperationException($"Playwright install exited with code {exitCode}");
        _playwrightReady = true;
    }

    [GeneratedRegex(@"article/p_(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArticlePathRegex();

    [GeneratedRegex(@"""ItemNumber""\s*:\s*""(\d{8})""", RegexOptions.IgnoreCase)]
    private static partial Regex ItemNumberJsonRegex();

    [GeneratedRegex(@"\{[^{}]*""Attributes""\s*:\s*\[[\s\S]*?\]\s*[^}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex ProductJsonRegex();
}
