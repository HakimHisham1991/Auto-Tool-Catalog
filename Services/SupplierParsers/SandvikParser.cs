using System.Text.Json;
using AutoToolCatalog.Models;
using Microsoft.Playwright;

namespace AutoToolCatalog.Services.SupplierParsers;

public class SandvikParser : BaseSupplierParser
{
    public SandvikParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public override string SupplierName => "SANDVIK";
    protected override string SearchBaseUrl => "https://www.sandvik.coromant.com";
    protected override TimeSpan PerAttemptTimeout => TimeSpan.FromSeconds(90);

    protected override async Task<ToolSpecResult> FetchSpecsCoreAsync(ToolRecord record, CancellationToken ct)
    {
        var partNo = System.Text.RegularExpressions.Regex.Replace(record.ToolDescription.Trim(), @"\s+", " ");
        var productUrl = $"{SearchBaseUrl}/en-us/product-details?c={Uri.EscapeDataString(partNo)}";

        var result = await FetchSpecsWithPlaywrightAsync(productUrl, record, ct);
        if (result != null && HasRequiredSpecs(result))
            return result;

        return ToolSpecResult.Failed("Could not extract Sandvik specs");
    }

    private static async Task<ToolSpecResult?> FetchSpecsWithPlaywrightAsync(string url, ToolRecord record, CancellationToken ct)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
            await page.GetByText("Cutting diameter", new() { Exact = false }).WaitForAsync(new LocatorWaitForOptions { Timeout = 20000 });

            // Try clicking Metric tab (best effort)
            try
            {
                await page.EvaluateAsync(@"() => {
                    const els = document.querySelectorAll('[role=tab], button, a, span, div');
                    for (const el of els) {
                        const txt = el.textContent.trim();
                        if (txt === 'Metric') { el.click(); return; }
                    }
                }");
                await Task.Delay(1500, ct);
            }
            catch { }

            // Extract specs - handles BOTH mm and inch (auto-converts inch→mm)
            var res = await ExtractSpecsFromPageAsync(page, record);
            await page.CloseAsync();
            return res;
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
            // Extract specs via JavaScript in the browser.
            // Search by CODE (e.g. "(DC)") not full label — labels have inconsistent spacing.
            // getVal: prefers "mm" values; if only "inch" found, converts inch→mm with 3 decimal precision.
            // getInt: for unitless specs like edge count.
            specs = await page.EvaluateAsync<JsonElement>(@"() => {
                const body = document.body.innerText;
                const idx = body.indexOf('Product data');
                const text = idx >= 0 ? body.substring(idx) : body;

                const getVal = (code) => {
                    const label = '(' + code + ')';
                    const i = text.indexOf(label);
                    if (i < 0) return null;
                    const chunk = text.substring(i + label.length, i + label.length + 80);

                    // 1. Try metric (mm)
                    const mm = chunk.match(/(-?\d+[,.]?\d*)\s*mm/);
                    if (mm) return mm[1].replace(',', '.') + ' mm';

                    // 2. Try inch and convert to mm (Sandvik uses 'inch' not 'in')
                    const inch = chunk.match(/(-?\d+[,.]?\d*)\s*inch/);
                    if (inch) {
                        const v = parseFloat(inch[1].replace(',', '.')) * 25.4;
                        if (Math.abs(v) < 0.01) return '0 mm';
                        // Smart rounding: snap to fewer decimals if floating-point noise
                        let r = Math.round(v * 1000) / 1000;
                        const r2 = Math.round(v * 100) / 100;
                        if (Math.abs(r - r2) < 0.002) r = r2;
                        const ri = Math.round(r);
                        if (Math.abs(r - ri) < 0.002) r = ri;
                        return (r % 1 === 0 ? r.toString() : parseFloat(r.toFixed(3)).toString()) + ' mm';
                    }
                    return null;
                };

                const getInt = (code) => {
                    const label = '(' + code + ')';
                    const i = text.indexOf(label);
                    if (i < 0) return null;
                    const chunk = text.substring(i + label.length, i + label.length + 40);
                    const m = chunk.match(/(\d{1,2})/);
                    return m ? m[1] : null;
                };

                return {
                    dc: getVal('DC'),
                    apmx: getVal('APMX'),
                    lu: getVal('LU'),
                    re: getVal('RE'),
                    zefp: getInt('ZEFP'),
                    oal: getVal('OAL') || getVal('LF'),
                    dconms: getVal('DCONMS')
                };
            }");
        }
        catch
        {
            specs = default;
        }

        if (specs.ValueKind == JsonValueKind.Object)
        {
            result.Spec1 = GetJsonString(specs, "dc") ?? "#NA";     // Tool Ø
            result.Spec5 = GetJsonString(specs, "oal") ?? "#NA";    // OAL
            result.Spec6 = GetJsonString(specs, "dconms") ?? "#NA"; // Shank/Bore Ø

            if (record.IsDrill)
            {
                // Drill: flute length = LU (usable length), corner rad = "--", edge count = 1
                result.Spec2 = GetJsonString(specs, "lu") ?? "#NA";  // Flute length (LU)
                result.Spec3 = "--";                                  // Corner rad not applicable
                result.Spec4 = "1";                                   // Drills have 1 cutting edge
            }
            else
            {
                // Endmill: flute length = APMX, corner rad = RE, edge count = ZEFP
                result.Spec2 = GetJsonString(specs, "apmx") ?? "#NA";  // Flute length
                result.Spec3 = GetJsonString(specs, "re") ?? "#NA";    // Corner rad
                result.Spec4 = GetJsonString(specs, "zefp") ?? "#NA";  // Edge count
            }
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
        (r.Spec2 != "#NA" && !string.IsNullOrEmpty(r.Spec2)) ||
        (r.Spec5 != "#NA" && !string.IsNullOrEmpty(r.Spec5)) ||
        (r.Spec4 != "#NA" && !string.IsNullOrEmpty(r.Spec4)) ||
        (r.Spec6 != "#NA" && !string.IsNullOrEmpty(r.Spec6));
}
