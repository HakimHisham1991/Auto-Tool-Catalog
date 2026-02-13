using System.Text.Json;
using AutoToolCatalog.Models;
using Microsoft.Playwright;

namespace AutoToolCatalog.Services.SupplierParsers;

public class WalterParser : BaseSupplierParser
{
    public WalterParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public override string SupplierName => "WALTER";
    protected override string SearchBaseUrl => "https://www.walter-tools.com";
    protected override TimeSpan PerAttemptTimeout => TimeSpan.FromSeconds(60);

    protected override async Task<ToolSpecResult> FetchSpecsCoreAsync(ToolRecord record, CancellationToken ct)
    {
        // Walter's website is a JS SPA. Use Playwright:
        // 1. Navigate to products page
        // 2. Reveal hidden search input, type part number, press Enter
        // 3. Extract specs from the rendered table
        var result = await FetchSpecsWithPlaywrightAsync(record, ct);
        if (result != null && HasRequiredSpecs(result))
            return result;

        return ToolSpecResult.Failed("Could not extract Walter specs");
    }

    private async Task<ToolSpecResult?> FetchSpecsWithPlaywrightAsync(ToolRecord record, CancellationToken ct)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();

            // Step 1: Load the products page
            await page.GotoAsync($"{SearchBaseUrl}/en-us/products",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            await Task.Delay(5000, ct);

            // Step 2: Click the search area to reveal hidden inputs
            await page.EvaluateAsync(@"() => {
                const triggers = document.querySelectorAll('.l-search-input, [class*=""search-icon""], [class*=""search-trigger""]');
                triggers.forEach(t => t.click());
            }");
            await Task.Delay(1500, ct);

            // Step 3: Force hidden search inputs visible and focus
            await page.EvaluateAsync(@"() => {
                const inputs = document.querySelectorAll('input[placeholder*=""earch""]');
                for (const inp of inputs) {
                    inp.style.display = 'block';
                    inp.style.visibility = 'visible';
                    inp.style.opacity = '1';
                    inp.style.position = 'relative';
                    inp.removeAttribute('hidden');
                    let p = inp.parentElement;
                    for (let i = 0; i < 10 && p; i++) {
                        p.style.display = '';
                        p.style.visibility = 'visible';
                        p.style.opacity = '1';
                        p.style.overflow = 'visible';
                        p.style.height = 'auto';
                        p = p.parentElement;
                    }
                    inp.focus();
                }
            }");
            await Task.Delay(500, ct);

            // Step 4: Fill and submit search
            var input = page.Locator("input[placeholder*='earch']").First;
            await input.FillAsync(record.ToolDescription, new LocatorFillOptions { Force = true });
            await Task.Delay(2000, ct); // Wait for autocomplete
            await input.PressAsync("Enter");
            await Task.Delay(8000, ct); // Wait for product page to render

            // Step 5: Extract specs
            var result = await ExtractSpecsFromPageAsync(page);
            await page.CloseAsync();
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<ToolSpecResult> ExtractSpecsFromPageAsync(IPage page)
    {
        var result = new ToolSpecResult { Success = true };
        JsonElement specs;
        try
        {
            // Walter spec table: "Description\tSymbol\tValue" (tab-separated rows)
            // Codes: Dc (diameter), R (radius), Lc (cutting length), l1 (overall length),
            //        d1 (shank diameter), Z (number of teeth)
            specs = await page.EvaluateAsync<JsonElement>(@"() => {
                const text = document.body.innerText;

                const getSpec = (code) => {
                    const re = new RegExp('^[^\\t\\n]*\\t' + code + '\\t([\\d,.]+\\s*(?:mm|inch)?)', 'm');
                    const m = text.match(re);
                    return m ? m[1].trim() : null;
                };

                const getCount = (code) => {
                    const re = new RegExp('^[^\\t\\n]*\\t' + code + '\\t(\\d+\\s*(?:EA)?)', 'm');
                    const m = text.match(re);
                    return m ? m[1].trim() : null;
                };

                return {
                    dc: getSpec('Dc'),
                    r: getSpec('R'),
                    lc: getSpec('Lc'),
                    l1: getSpec('l1'),
                    d1: getSpec('d1'),
                    z: getCount('Z')
                };
            }");
        }
        catch
        {
            specs = default;
        }

        if (specs.ValueKind == JsonValueKind.Object)
        {
            result.Spec1 = GetJsonString(specs, "dc") ?? "#NA";  // Tool Ø
            result.Spec2 = GetJsonString(specs, "lc") ?? "#NA";  // Flute/cutting length
            result.Spec3 = GetJsonString(specs, "r") ?? "#NA";   // Corner radius
            result.Spec4 = GetJsonString(specs, "z") ?? "#NA";   // Edge count
            result.Spec5 = GetJsonString(specs, "l1") ?? "#NA";  // OAL
            result.Spec6 = GetJsonString(specs, "d1") ?? "#NA";  // Shank/bore Ø
        }

        return result;
    }

    private static string? GetJsonString(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var p)) return null;
        if (p.ValueKind == JsonValueKind.String) return p.GetString();
        if (p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined) return null;
        return p.ToString();
    }

    private static bool HasRequiredSpecs(ToolSpecResult r) =>
        (r.Spec1 != "#NA" && !string.IsNullOrEmpty(r.Spec1)) ||
        (r.Spec5 != "#NA" && !string.IsNullOrEmpty(r.Spec5)) ||
        (r.Spec3 != "#NA" && !string.IsNullOrEmpty(r.Spec3));
}
