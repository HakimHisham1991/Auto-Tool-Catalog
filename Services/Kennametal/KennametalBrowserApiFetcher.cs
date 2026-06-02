using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AutoToolCatalog.Services.Kennametal;

/// <summary>
/// Resolves Kennametal product IDs via site search when HttpClient search is blocked.
/// </summary>
internal static partial class KennametalBrowserApiFetcher
{
    private const string KennametalHome = "https://www.kennametal.com/us/en.html";
    private const int NavigationTimeoutMs = 120_000;
    private static readonly SemaphoreSlim BrowserGate = new(1, 1);
    private static bool _playwrightReady;

    public sealed record ResolvedProduct(string ProductId, string ProductUrl);

    public static async Task<ResolvedProduct?> ResolveByPartNumberAsync(string partNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(partNumber))
            return null;

        await BrowserGate.WaitAsync(ct);
        try
        {
            await EnsurePlaywrightAsync(ct);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();

            await page.GotoAsync(KennametalHome, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = NavigationTimeoutMs
            });
            ct.ThrowIfCancellationRequested();
            await TryDismissCookiesAsync(page);

            var searchFilled = false;
            foreach (var selector in new[]
            {
                "input[placeholder*='Search' i]",
                "input[type='search']",
                "input[name='search']",
                "#search"
            })
            {
                var inputs = page.Locator(selector);
                if (await inputs.CountAsync() == 0)
                    continue;

                await inputs.First.FillAsync(partNumber.Trim());
                await inputs.First.PressAsync("Enter");
                searchFilled = true;
                break;
            }

            if (!searchFilled)
                return null;

            try
            {
                await page.WaitForURLAsync(
                    url => url.Contains("/products/", StringComparison.OrdinalIgnoreCase) &&
                           ProductPageRegex().IsMatch(url),
                    new PageWaitForURLOptions { Timeout = NavigationTimeoutMs });
            }
            catch
            {
                // Some searches do not redirect; results may show links instead.
                await page.WaitForTimeoutAsync(5000);
            }

            ct.ThrowIfCancellationRequested();

            // If we didn't land on a product page, click/navigate the first result link.
            if (!page.Url.Contains("/products/", StringComparison.OrdinalIgnoreCase) ||
                !ProductPageRegex().IsMatch(page.Url))
            {
                try
                {
                    var firstLink = page.Locator("a[href*='/products/'][href*='.html']").First;
                    await firstLink.WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Attached,
                        Timeout = 30_000
                    });
                    var href = await firstLink.GetAttributeAsync("href");
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        var target = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? href
                            : $"https://www.kennametal.com{href}";
                        await page.GotoAsync(target, new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = NavigationTimeoutMs
                        });
                    }
                }
                catch
                {
                    // If this fails, we will fall back to HTML-based matching below.
                }
            }

            var productId = ExtractProductIdFromUrl(page.Url);
            if (string.IsNullOrWhiteSpace(productId))
            {
                var html = await page.ContentAsync();
                var match = ProductPageRegex().Match(html);
                if (!match.Success)
                    return null;
                productId = match.Groups[1].Value;
            }

            var productUrl = page.Url.Contains(productId, StringComparison.Ordinal)
                ? page.Url
                : $"https://www.kennametal.com/us/en/products/p.product.{productId}.html";

            return new ResolvedProduct(productId, productUrl);
        }
        finally
        {
            BrowserGate.Release();
        }
    }

    private static async Task TryDismissCookiesAsync(IPage page)
    {
        foreach (var selector in new[]
        {
            "#onetrust-accept-btn-handler",
            "button:has-text('Accept All')",
            "button:has-text('Accept all')",
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
                // try next
            }
        }
    }

    private static string? ExtractProductIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var match = ProductPageRegex().Match(url);
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

    [GeneratedRegex(@"\.(\d{5,})\.html", RegexOptions.IgnoreCase)]
    private static partial Regex ProductPageRegex();
}
