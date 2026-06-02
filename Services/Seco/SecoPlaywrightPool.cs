using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AutoToolCatalog.Services.Seco;

/// <summary>
/// Reuses one Chromium instance for SECO in-page API calls (GetFullProduct returns 405 outside the browser).
/// </summary>
internal static partial class SecoPlaywrightPool
{
    private const string BaseUrl = "https://www.secotools.com";
    private const int NavigationTimeoutMs = 45_000;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private static IBrowserContext? _context;
    private static IPage? _sessionPage;
    private static bool _playwrightReady;
    private static bool _sessionWarmedUp;

    public sealed record BrowserFetchResult(string ItemNumber, string ProductUrl, string Json);

    public static Task<BrowserFetchResult?> FetchAsync(
        string? webpageLink,
        string designation,
        string? knownItemNumber,
        CancellationToken ct)
    {
        var itemNumber = knownItemNumber ?? SecoHttpSession.ExtractItemNumberFromUrl(webpageLink);
        if (!string.IsNullOrWhiteSpace(itemNumber))
            return FetchByItemNumberAsync(itemNumber, ct);

        if (!string.IsNullOrWhiteSpace(designation))
            return SearchAndFetchAsync(designation.Trim(), ct);

        if (!string.IsNullOrWhiteSpace(webpageLink))
            return SearchAndFetchAsync(webpageLink.Trim(), ct);

        return Task.FromResult<BrowserFetchResult?>(null);
    }

    public static async Task<BrowserFetchResult?> FetchByItemNumberAsync(string itemNumber, CancellationToken ct)
    {
        var json = await FetchGetFullProductJsonAsync(itemNumber, ct);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var productUrl = $"{BaseUrl}/article/p_{itemNumber}";
        return new BrowserFetchResult(itemNumber, productUrl, json);
    }

    public static async Task<string?> FetchGetFullProductJsonAsync(string itemNumber, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await EnsureBrowserAsync(ct);
            var page = await GetSessionPageAsync(ct);

            var json = await FetchGetFullProductViaEvaluateAsync(page, itemNumber);
            if (!string.IsNullOrWhiteSpace(json) && json.Contains("Attributes", StringComparison.OrdinalIgnoreCase))
                return json;

            var productUrl = $"{BaseUrl}/article/p_{itemNumber}";
            json = await LoadProductJsonFromPageAsync(page, productUrl, itemNumber, ct);
            return json;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<BrowserFetchResult?> SearchAndFetchAsync(string designation, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await EnsureBrowserAsync(ct);
            var page = await _context!.NewPageAsync();
            try
            {
                var (itemNumber, productUrl, json) = await SearchAndLoadAsync(page, designation, ct);
                if (string.IsNullOrWhiteSpace(itemNumber) || string.IsNullOrWhiteSpace(json))
                    return null;

                productUrl ??= $"{BaseUrl}/article/p_{itemNumber}";
                return new BrowserFetchResult(itemNumber, productUrl, json);
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<(string? ItemNumber, string? ProductUrl, string? Json)> SearchAndLoadAsync(
        IPage page, string designation, CancellationToken ct)
    {
        var searchUrl = $"{BaseUrl}/search?q={Uri.EscapeDataString(designation)}";
        try
        {
            await page.GotoAsync(searchUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = NavigationTimeoutMs
            });
            ct.ThrowIfCancellationRequested();
            await TryDismissCookiesAsync(page);

            var fromUrl = ExtractItemNumberFromUrl(page.Url);
            if (!string.IsNullOrWhiteSpace(fromUrl))
            {
                var productUrl = page.Url.Contains("article/p_", StringComparison.OrdinalIgnoreCase)
                    ? page.Url
                    : $"{BaseUrl}/article/p_{fromUrl}";
                var json = await LoadProductJsonFromPageAsync(page, productUrl, fromUrl, ct);
                return (fromUrl, productUrl, json);
            }
        }
        catch
        {
            // fall through to search box UI
        }

        await WarmupPageAsync(page, ct);

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
                    Timeout = 15_000
                });
            }
            catch
            {
                return (null, null, null);
            }
        }

        await searchInput.FillAsync(designation);
        await searchInput.PressAsync("Enter");

        try
        {
            await page.WaitForURLAsync(
                url => url.Contains("article/p_", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = NavigationTimeoutMs });
        }
        catch
        {
            await page.WaitForTimeoutAsync(2000);
        }

        ct.ThrowIfCancellationRequested();

        var itemNumber = ExtractItemNumberFromUrl(page.Url);
        if (string.IsNullOrWhiteSpace(itemNumber))
            itemNumber = await TryGetItemNumberFromResultLinksAsync(page);
        if (string.IsNullOrWhiteSpace(itemNumber))
        {
            var html = await page.ContentAsync();
            itemNumber = PickItemNumberFromHtml(html, designation);
        }

        if (string.IsNullOrWhiteSpace(itemNumber))
            return (null, null, null);

        var url = $"{BaseUrl}/article/p_{itemNumber}";
        var productJson = await LoadProductJsonFromPageAsync(page, url, itemNumber, ct);
        return (itemNumber, url, productJson);
    }

    private static async Task<IPage> GetSessionPageAsync(CancellationToken ct)
    {
        if (_sessionPage is { IsClosed: false })
            return _sessionPage;

        _sessionPage = await _context!.NewPageAsync();
        await WarmupPageAsync(_sessionPage, ct);
        _sessionWarmedUp = true;
        return _sessionPage;
    }

    private static async Task WarmupPageAsync(IPage page, CancellationToken ct)
    {
        if (_sessionWarmedUp && ReferenceEquals(page, _sessionPage))
            return;

        await page.GotoAsync(BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = NavigationTimeoutMs
        });
        ct.ThrowIfCancellationRequested();
        await TryDismissCookiesAsync(page);

        if (ReferenceEquals(page, _sessionPage))
            _sessionWarmedUp = true;
    }

    private static async Task<string?> LoadProductJsonFromPageAsync(
        IPage page, string productUrl, string itemNumber, CancellationToken ct)
    {
        var fromEvaluate = await FetchGetFullProductViaEvaluateAsync(page, itemNumber);
        if (!string.IsNullOrWhiteSpace(fromEvaluate))
            return fromEvaluate;

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
            // fall through
        }

        var html = await page.ContentAsync();
        return ExtractProductJsonFromHtml(html);
    }

    private static async Task<string?> FetchGetFullProductViaEvaluateAsync(IPage page, string itemNumber) =>
        await page.EvaluateAsync<string?>(@"
            async (itemNumber) => {
                const url = 'https://www.secotools.com/core/api/Products/GetFullProduct?itemNumber=' + encodeURIComponent(itemNumber);
                const response = await fetch(url, {
                    headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
                });
                if (!response.ok) return null;
                const text = await response.text();
                return text && text.includes('Attributes') ? text : null;
            }
        ", itemNumber);

    private static async Task<string?> TryGetItemNumberFromResultLinksAsync(IPage page)
    {
        try
        {
            var links = page.Locator("a[href*='article/p_']");
            await links.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = 15_000
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
                await page.WaitForTimeoutAsync(300);
                return;
            }
            catch
            {
                // try next selector
            }
        }
    }

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
        return firstArticle.Success ? firstArticle.Groups[1].Value : null;
    }

    private static string? ExtractItemNumberFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var match = ArticlePathRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task EnsureBrowserAsync(CancellationToken ct)
    {
        await EnsurePlaywrightAsync(ct);
        if (_browser is { IsConnected: true })
            return;

        _browser = await _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _context = await _browser.NewContextAsync();
        _sessionPage = null;
        _sessionWarmedUp = false;
    }

    private static async Task EnsurePlaywrightAsync(CancellationToken ct)
    {
        if (_playwrightReady) return;
        ct.ThrowIfCancellationRequested();
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
            throw new InvalidOperationException($"Playwright install exited with code {exitCode}");
        _playwright = await Playwright.CreateAsync();
        _playwrightReady = true;
    }

    [GeneratedRegex(@"article/p_(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArticlePathRegex();

    [GeneratedRegex(@"""ItemNumber""\s*:\s*""(\d{8})""", RegexOptions.IgnoreCase)]
    private static partial Regex ItemNumberJsonRegex();

    [GeneratedRegex(@"\{[^{}]*""Attributes""\s*:\s*\[[\s\S]*?\]\s*[^}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex ProductJsonRegex();
}
