using System.Text.Json;
using AutoToolCatalog.Models;
using HtmlAgilityPack;
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
        var partNo = record.ToolDescription.Trim();

        // 1. Try direct product URL (hard-coded for known items)
        var productUrl = TryDirectProductUrl(partNo);
        if (!string.IsNullOrEmpty(productUrl))
        {
            var productDoc = await FetchHtmlAsync(productUrl, ct);
            if (productDoc != null)
            {
                var result = ParseSpecTable(productDoc, record);
                if (HasRequiredSpecs(result)) return result;
            }
        }

        // 2. Use Playwright to search Kennametal and find the product page
        var playwrightResult = await FetchSpecsWithPlaywrightAsync(record, ct);
        if (playwrightResult != null && HasRequiredSpecs(playwrightResult))
            return playwrightResult;

        // 3. Fallback: HttpClient search (rarely works for JS-rendered search)
        var searchUrl = $"{SearchBaseUrl}/us/en/search.html?q={Uri.EscapeDataString(partNo)}";
        var doc = await FetchHtmlAsync(searchUrl, ct);
        if (doc == null) return ToolSpecResult.Failed("Could not load search page");

        productUrl = TryFindProductUrl(doc, partNo);
        if (!string.IsNullOrEmpty(productUrl))
        {
            var productDoc = await FetchHtmlAsync(productUrl, ct);
            if (productDoc != null)
                return ParseSpecTable(productDoc, record);
        }

        return ParseSpecTable(doc, record);
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
            // Kennametal spec table: <tr class="... metric"><td class="spec-label">[code] label</td><td class="spec-value">value</td></tr>
            // Extract from metric rows only, skipping inch rows
            specs = await page.EvaluateAsync<JsonElement>(@"() => {
                const rows = document.querySelectorAll('tr');
                const metricSpecs = {};
                for (const row of rows) {
                    const cls = row.className || '';
                    // Skip inch rows
                    if (cls.includes('inch')) continue;
                    const labelCell = row.querySelector('td.spec-label');
                    const valueCell = row.querySelector('td.spec-value');
                    if (!labelCell || !valueCell) continue;
                    const label = labelCell.textContent.trim();
                    const value = valueCell.textContent.trim();
                    if (!value) continue;
                    // Map by spec code in brackets, e.g. '[D1] Drill Diameter M' → key 'D1'
                    const codeMatch = label.match(/^\[(\w+)\]/);
                    if (codeMatch) {
                        metricSpecs[codeMatch[1]] = value;
                    }
                    // Also store the full label for fallback matching
                    metricSpecs['_' + label] = value;
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
                // Facemill / Insert Endmill:
                result.Spec1 = GetJsonString(specs, "D1MAX") ?? "#NA";   // Tool Ø = [D1MAX] Maximum Cutting Diameter
                result.Spec2 = GetJsonString(specs, "AP1MAX") ?? "#NA";  // Flute length = [AP1MAX] 1st Maximum Cutting Depth
                result.Spec3 = "--";                                      // Corner rad = --
                result.Spec4 = GetJsonString(specs, "Z") ?? "#NA";      // Edge count = [Z] Number of Flutes
                result.Spec5 = GetJsonString(specs, "L") ?? "#NA";      // OAL = [L] Overall Length
                result.Spec6 = GetJsonString(specs, "D6") ?? "#NA";     // Shank/Bore Ø = [D6] Hub Diameter
            }
            else if (record.IsDrill)
            {
                // Solid Drill:
                result.Spec1 = GetJsonString(specs, "D1") ?? "#NA";   // Tool Ø
                result.Spec2 = GetJsonString(specs, "L3") ?? "#NA";   // Flute length
                result.Spec3 = "--";                                    // Corner rad
                result.Spec4 = "1";                                     // Edge count
                result.Spec5 = GetJsonString(specs, "L") ?? "#NA";    // OAL
                result.Spec6 = GetJsonString(specs, "D") ?? "#NA";    // Shank/Bore Ø
            }
            else
            {
                // Solid Endmill:
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

    private static string? TryDirectProductUrl(string partNo)
    {
        var directUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["H1TE4RA0400N006HBR025M"] = "https://www.kennametal.com/us/en/products/p.harvi-i-te-radiused-4-flutes-necked-weldon-shank-metric.6767970.html"
        };
        return directUrls.TryGetValue(partNo, out var url) ? url : null;
    }

    private static bool HasRequiredSpecs(ToolSpecResult r) =>
        (r.Spec1 != "#NA" && !string.IsNullOrEmpty(r.Spec1)) ||
        (r.Spec5 != "#NA" && !string.IsNullOrEmpty(r.Spec5)) ||
        (r.Spec4 != "#NA" && !string.IsNullOrEmpty(r.Spec4));

    private static string? TryFindProductUrl(HtmlDocument doc, string toolDesc)
    {
        var partUpper = toolDesc.ToUpperInvariant();
        var keywords = partUpper.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'products') or contains(@href,'/product') or contains(@href,'.html')]");
        if (links == null) return null;
        foreach (var link in links.Take(50))
        {
            var href = link.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href) || !href.Contains("products", StringComparison.OrdinalIgnoreCase)) continue;
            var fullUrl = href.StartsWith("http") ? href : "https://www.kennametal.com" + (href.StartsWith("/") ? href : "/" + href);
            var text = (link.InnerText + " " + href).ToUpperInvariant();
            if (keywords.Any(k => text.Contains(k)))
                return fullUrl;
        }
        var first = links.FirstOrDefault(l => l.GetAttributeValue("href", "").Contains("products", StringComparison.OrdinalIgnoreCase));
        var firstHref = first?.GetAttributeValue("href", "");
        return string.IsNullOrEmpty(firstHref) ? null : (firstHref.StartsWith("http") ? firstHref : "https://www.kennametal.com" + (firstHref.StartsWith("/") ? firstHref : "/" + firstHref));
    }

    private ToolSpecResult ParseSpecTable(HtmlDocument doc, ToolRecord record)
    {
        var result = new ToolSpecResult { Success = true };

        if (record.IsFacemillOrInsertEndmill)
        {
            result.Spec1 = ExtractKennametalSpec(doc, "[D1MAX] Maximum Cutting Diameter") ?? ExtractSpec(doc, new[] { "D1MAX", "Maximum Cutting Diameter" }) ?? "#NA";
            result.Spec2 = ExtractKennametalSpec(doc, "[AP1MAX] 1st Maximum Cutting Depth") ?? ExtractSpec(doc, new[] { "AP1MAX", "AP1 max" }) ?? "#NA";
            result.Spec3 = "--";
            result.Spec4 = ExtractKennametalSpec(doc, "[Z] Number of Flutes") ?? ExtractSpec(doc, new[] { "Z", "Number of Flutes" }) ?? "#NA";
            result.Spec5 = ExtractKennametalSpec(doc, "[L] Overall Length") ?? ExtractSpec(doc, new[] { "Overall Length", "L" }) ?? "#NA";
            result.Spec6 = ExtractKennametalSpec(doc, "[D6] Hub Diameter") ?? ExtractSpec(doc, new[] { "D6", "Hub Diameter" }) ?? "#NA";
        }
        else if (record.IsEndmill)
        {
            result.Spec1 = ExtractKennametalSpec(doc, "[D1] Effective Cutting Diameter") ?? ExtractSpec(doc, new[] { "D1", "D", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractKennametalSpec(doc, "[AP1MAX] 1st Maximum Cutting Depth") ?? ExtractSpec(doc, new[] { "AP1MAX", "AP1 max" }) ?? "#NA";
            result.Spec3 = ExtractKennametalSpec(doc, "[Re] Corner Radius") ?? ExtractSpec(doc, new[] { "Re", "corner", "radius" }) ?? "#NA";
            result.Spec4 = ExtractKennametalSpec(doc, "[Z] Number of Flutes") ?? ExtractSpec(doc, new[] { "Z", "Number of Flutes" }) ?? "#NA";
            result.Spec5 = ExtractKennametalSpec(doc, "[L] Overall Length") ?? ExtractSpec(doc, new[] { "Overall Length", "L", "l1" }) ?? "#NA";
            result.Spec6 = ExtractKennametalSpec(doc, "[D] Adapter / Shank / Bore Diameter") ?? ExtractSpec(doc, new[] { "Adapter / Shank / Bore Diameter", "Shank" }) ?? "#NA";
        }
        else if (record.IsDrill)
        {
            result.Spec1 = ExtractKennametalSpec(doc, "[D1] Drill Diameter") ?? ExtractSpec(doc, new[] { "D1", "Drill Diameter" }) ?? "#NA";
            result.Spec2 = ExtractKennametalSpec(doc, "[L3] Flute Length") ?? ExtractSpec(doc, new[] { "L3", "Flute Length" }) ?? "#NA";
            result.Spec3 = "--";
            result.Spec4 = "1";
            result.Spec5 = ExtractKennametalSpec(doc, "[L] Overall Length") ?? ExtractSpec(doc, new[] { "Overall Length", "L" }) ?? "#NA";
            result.Spec6 = ExtractKennametalSpec(doc, "[D] Adapter / Shank / Bore Diameter") ?? ExtractSpec(doc, new[] { "Adapter / Shank / Bore Diameter", "Shank", "D" }) ?? "#NA";
        }
        else
        {
            result.Spec1 = ExtractSpec(doc, new[] { "D1", "D", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractSpec(doc, new[] { "L4", "length", "flute" }) ?? "#NA";
            result.Spec5 = ExtractSpec(doc, new[] { "L", "l1", "length", "overall" }) ?? "#NA";
            result.Spec6 = ExtractSpec(doc, new[] { "Adapter / Shank / Bore Diameter", "Adapter", "Shank", "Bore Diameter", "D", "d1" }) ?? "#NA";
        }

        return result;
    }

    private static string? ExtractKennametalSpec(HtmlDocument doc, string label)
    {
        var labelCells = doc.DocumentNode.SelectNodes("//td[contains(@class,'spec-label')]");
        if (labelCells != null)
        {
            var labelUpper = label.Trim().ToUpperInvariant();
            foreach (var cell in labelCells)
            {
                if (!cell.InnerText.Trim().ToUpperInvariant().Contains(labelUpper)) continue;
                var row = cell.SelectSingleNode("./parent::tr") ?? cell.ParentNode;
                if (row == null || row.Name != "tr") continue;
                if (row.GetAttributeValue("class", "").Contains("inch", StringComparison.OrdinalIgnoreCase)) continue;
                var valueCell = row.SelectSingleNode(".//td[contains(@class,'spec-value')]");
                var value = valueCell?.InnerText.Trim();
                if (!string.IsNullOrWhiteSpace(value)) return NormalizeValue(value);
            }
        }
        var body = doc.DocumentNode.InnerText;
        var escaped = System.Text.RegularExpressions.Regex.Escape(label.Trim());
        var m = System.Text.RegularExpressions.Regex.Match(body, escaped + @"[\s\S]{0,50}?(\d+[,.]?\d*)\s*(?:mm|in)|" + escaped + @"[\s\S]{0,30}?(\d{1,2})(?:\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var val = m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value.Trim();
            return string.IsNullOrEmpty(val) ? null : NormalizeValue(val.Contains('.') || val.Contains(',') ? val + " mm" : val);
        }
        return null;
    }
}
