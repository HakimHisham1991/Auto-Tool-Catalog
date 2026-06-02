using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AutoToolCatalog.Services.TaeguTec;

/// <summary>
/// TaeguTec e-catalog is behind Cloudflare; specs are server-rendered HTML (no public JSON API).
/// </summary>
internal static partial class TaeguTecBrowserFetcher
{
    private const string CatalogBase = "https://www.imc-companies.com/TaeguTec/ttkCatalog/";
    private const int NavigationTimeoutMs = 120_000;
    private static readonly SemaphoreSlim BrowserGate = new(1, 1);
    private static bool _playwrightReady;

    public sealed record FetchResult(string Html, string ProductUrl, string CatalogId);

    public static async Task<FetchResult?> FetchItemByCatalogIdAsync(string catalogId, CancellationToken ct)
    {
        var url = $"{CatalogBase}item.aspx?cat={catalogId}&isoD=1";
        return await FetchPageAsync(url, catalogId, ct);
    }

    public static async Task<FetchResult?> SearchAndFetchAsync(string designation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(designation))
            return null;

        await BrowserGate.WaitAsync(ct);
        try
        {
            await EnsurePlaywrightAsync(ct);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();

            await page.GotoAsync(CatalogBase, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = NavigationTimeoutMs
            });
            ct.ThrowIfCancellationRequested();
            await page.WaitForTimeoutAsync(3000);

            var searchUrl =
                $"{CatalogBase}search.aspx?searchText={Uri.EscapeDataString(designation.Trim())}";
            await page.GotoAsync(searchUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = NavigationTimeoutMs
            });
            ct.ThrowIfCancellationRequested();
            await page.WaitForTimeoutAsync(2000);

            var normalized = NormalizeToken(designation);
            var links = page.Locator("a[href*='item.aspx?cat=']");
            var count = await links.CountAsync();
            for (var i = 0; i < count; i++)
            {
                var link = links.Nth(i);
                var href = await link.GetAttributeAsync("href");
                var text = NormalizeToken(await link.InnerTextAsync());
                if (string.IsNullOrWhiteSpace(href))
                    continue;

                var catMatch = CatalogIdInUrlRegex().Match(href);
                if (!catMatch.Success)
                    continue;

                if (text.Contains(normalized, StringComparison.Ordinal) ||
                    normalized.Contains(text, StringComparison.Ordinal) ||
                    i == 0)
                {
                    var target = ToAbsoluteUrl(href);
                    await page.GotoAsync(target, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = NavigationTimeoutMs
                    });
                    var html = await page.ContentAsync();
                    return new FetchResult(html, page.Url, catMatch.Groups[1].Value);
                }
            }

            return null;
        }
        finally
        {
            BrowserGate.Release();
        }
    }

    private static async Task<FetchResult?> FetchPageAsync(string url, string catalogId, CancellationToken ct)
    {
        await BrowserGate.WaitAsync(ct);
        try
        {
            await EnsurePlaywrightAsync(ct);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = NavigationTimeoutMs
            });
            ct.ThrowIfCancellationRequested();
            await WaitForProductPageAsync(page);
            var html = await page.ContentAsync();
            return new FetchResult(html, page.Url, catalogId);
        }
        finally
        {
            BrowserGate.Release();
        }
    }

    private static string ToAbsoluteUrl(string href)
    {
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return href;
        var path = href.StartsWith('/') ? href : "/TaeguTec/ttkCatalog/" + href.TrimStart('/');
        return "https://www.imc-companies.com" + path;
    }

    private static string NormalizeToken(string value) =>
        Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);

    private static async Task WaitForProductPageAsync(IPage page)
    {
        foreach (var selector in new[]
        {
            "text=Item Designation",
            "text=Family Designation",
            "table"
        })
        {
            try
            {
                await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = 90_000 });
                await page.WaitForTimeoutAsync(1000);
                return;
            }
            catch
            {
                // try next
            }
        }

        await page.WaitForTimeoutAsync(5000);
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

    [GeneratedRegex(@"[?&]cat=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CatalogIdInUrlRegex();
}
