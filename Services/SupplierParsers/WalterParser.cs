using System.Text.Json;
using AutoToolCatalog.Models;
using Microsoft.Playwright;

namespace AutoToolCatalog.Services.SupplierParsers;

public class WalterParser : BaseSupplierParser
{
    public WalterParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public override string SupplierName => "WALTER";
    protected override string SearchBaseUrl => "https://www.walter-tools.com";
    protected override TimeSpan PerAttemptTimeout => TimeSpan.FromSeconds(90);

    protected override async Task<ToolSpecResult> FetchSpecsCoreAsync(ToolRecord record, CancellationToken ct)
    {
        // Walter's website is a JS SPA. Use Playwright with the direct product URL:
        // https://www.walter-tools.com/en-us/search/product/{partNumber}
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

            // Navigate directly to the product search result URL
            var partNo = record.ToolDescription.Trim();
            var url = $"{SearchBaseUrl}/en-us/search/product/{Uri.EscapeDataString(partNo.ToLowerInvariant())}";
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
            await Task.Delay(10000, ct); // Wait for Angular JS rendering

            // Verify specs are rendered; if not, wait a bit more
            var hasSpecs = await page.EvaluateAsync<bool>("() => document.body.innerText.includes('\\tDc\\t')");
            if (!hasSpecs)
                await Task.Delay(5000, ct);

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
