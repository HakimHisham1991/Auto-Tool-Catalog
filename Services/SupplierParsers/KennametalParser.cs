using System.Text.Json;
using AutoToolCatalog.Models;
using Microsoft.Playwright;

namespace AutoToolCatalog.Services.SupplierParsers;

public class KennametalParser : BaseSupplierParser
{
    public KennametalParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public override string SupplierName => "KENNAMETAL";
    protected override string SearchBaseUrl => "https://www.kennametal.com";
    protected override TimeSpan PerAttemptTimeout => TimeSpan.FromSeconds(60);

    protected override async Task<ToolSpecResult> FetchSpecsCoreAsync(ToolRecord record, CancellationToken ct)
    {
        var playwrightResult = await FetchSpecsWithPlaywrightAsync(record, ct);
        if (playwrightResult != null && HasRequiredSpecs(playwrightResult))
            return playwrightResult;

        return ToolSpecResult.Failed("Could not extract Kennametal specs");
    }

    private async Task<ToolSpecResult?> FetchSpecsWithPlaywrightAsync(ToolRecord record, CancellationToken ct)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();

            var partNo = record.ToolDescription.Trim();

            // Step 1: Navigate to Kennametal homepage
            await page.GotoAsync($"{SearchBaseUrl}/us/en.html", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            await Task.Delay(3000, ct);

            // Step 2: Fill search input with part number and press Enter
            var searchInput = page.Locator("input#query, input[name='query']").First;
            await searchInput.FillAsync(partNo);
            await Task.Delay(1000, ct);
            await searchInput.PressAsync("Enter");
            await Task.Delay(8000, ct); // Wait for redirect to product page

            // Step 3: Check if we landed on a product page
            if (!page.Url.Contains("/products/p."))
            {
                // We're on a search results page. Look for /products/p. links in the results.
                var productUrl = await page.EvaluateAsync<string?>(@"() => {
                    const links = [...document.querySelectorAll('a')];
                    for (const a of links) {
                        if (a.href && a.href.includes('/products/p.') && a.href.includes('.html')) {
                            return a.href;
                        }
                    }
                    return null;
                }");

                if (string.IsNullOrEmpty(productUrl))
                {
                    // Fallback: try the store search which can list individual products
                    await page.GotoAsync($"{SearchBaseUrl}/store/us/en/kmt/search?q={Uri.EscapeDataString(partNo)}",
                        new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
                    await Task.Delay(10000, ct);

                    productUrl = await page.EvaluateAsync<string?>(@"() => {
                        const links = [...document.querySelectorAll('a')];
                        for (const a of links) {
                            if (a.href && a.href.includes('/products/p.') && a.href.includes('.html')) {
                                return a.href;
                            }
                        }
                        return null;
                    }");
                }

                if (string.IsNullOrEmpty(productUrl))
                {
                    await page.CloseAsync();
                    return null;
                }

                // Navigate to the found product page
                await page.GotoAsync(productUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
                await Task.Delay(5000, ct);
            }

            // Step 4: Extract specs from the rendered product page
            var result = await ExtractSpecsFromPageAsync(page, record);
            await page.CloseAsync();
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<ToolSpecResult> ExtractSpecsFromPageAsync(IPage page, ToolRecord record)
    {
        var result = new ToolSpecResult { Success = true };
        JsonElement specs;
        try
        {
            specs = await page.EvaluateAsync<JsonElement>(@"() => {
                const rows = document.querySelectorAll('tr');
                const metricSpecs = {};
                for (const row of rows) {
                    const cls = row.className || '';
                    if (cls.includes('inch')) continue;
                    const labelCell = row.querySelector('td.spec-label');
                    const valueCell = row.querySelector('td.spec-value');
                    if (!labelCell || !valueCell) continue;
                    const label = labelCell.textContent.trim();
                    const value = valueCell.textContent.trim();
                    if (!value) continue;
                    const codeMatch = label.match(/^\[(\w+)\]/);
                    if (codeMatch) {
                        metricSpecs[codeMatch[1]] = value;
                    }
                }
                return metricSpecs;
            }");
        }
        catch
        {
            specs = default;
        }

        if (specs.ValueKind == JsonValueKind.Object)
        {
            if (record.IsFacemillOrInsertEndmill)
            {
                result.Spec1 = GetJsonString(specs, "D1MAX") ?? "#NA";   // Tool Ø = [D1MAX] Maximum Cutting Diameter
                result.Spec2 = GetJsonString(specs, "AP1MAX") ?? "#NA";  // Flute length = [AP1MAX] 1st Maximum Cutting Depth
                result.Spec3 = "--";                                      // Corner rad = --
                result.Spec4 = GetJsonString(specs, "Z") ?? "#NA";      // Edge count = [Z] Number of Flutes
                result.Spec5 = GetJsonString(specs, "L") ?? "#NA";      // OAL = [L] Overall Length
                result.Spec6 = GetJsonString(specs, "D6") ?? "#NA";     // Shank/Bore Ø = [D6] Hub Diameter
            }
            else if (record.IsDrill)
            {
                result.Spec1 = GetJsonString(specs, "D1") ?? "#NA";   // Tool Ø
                result.Spec2 = GetJsonString(specs, "L3") ?? "#NA";   // Flute length
                result.Spec3 = "--";                                    // Corner rad
                result.Spec4 = "1";                                     // Edge count
                result.Spec5 = GetJsonString(specs, "L") ?? "#NA";    // OAL
                result.Spec6 = GetJsonString(specs, "D") ?? "#NA";    // Shank/Bore Ø
            }
            else
            {
                result.Spec1 = GetJsonString(specs, "D1") ?? "#NA";     // Tool Ø
                result.Spec2 = GetJsonString(specs, "AP1MAX") ?? "#NA"; // Flute/cutting length
                result.Spec3 = GetJsonString(specs, "Re") ?? "#NA";    // Corner rad
                result.Spec4 = GetJsonString(specs, "Z") ?? "#NA";     // Edge count
                result.Spec5 = GetJsonString(specs, "L") ?? "#NA";     // OAL
                result.Spec6 = GetJsonString(specs, "D") ?? "#NA";     // Shank/Bore Ø
            }
        }

        return result;
    }

    private static string? GetJsonString(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var p)) return null;
        if (p.ValueKind == JsonValueKind.String)
        {
            var val = p.GetString();
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }
        if (p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined) return null;
        return p.ToString();
    }

    private static bool HasRequiredSpecs(ToolSpecResult r) =>
        (r.Spec1 != "#NA" && !string.IsNullOrEmpty(r.Spec1)) ||
        (r.Spec5 != "#NA" && !string.IsNullOrEmpty(r.Spec5)) ||
        (r.Spec4 != "#NA" && !string.IsNullOrEmpty(r.Spec4));
}
