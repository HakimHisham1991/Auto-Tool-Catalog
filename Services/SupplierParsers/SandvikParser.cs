using System.Text.Json;
using AutoToolCatalog.Models;
using HtmlAgilityPack;
using Microsoft.Playwright;

namespace AutoToolCatalog.Services.SupplierParsers;

public class SandvikParser : BaseSupplierParser
{
    public SandvikParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public override string SupplierName => "SANDVIK";
    protected override string SearchBaseUrl => "https://www.sandvik.coromant.com";

    protected override async Task<ToolSpecResult> FetchSpecsCoreAsync(ToolRecord record, CancellationToken ct)
    {
        var partNo = System.Text.RegularExpressions.Regex.Replace(record.ToolDescription.Trim(), @"\s+", " ");
        var productUrl = $"{SearchBaseUrl}/en-us/product-details?c={Uri.EscapeDataString(partNo)}";

        var result = await FetchSpecsWithPlaywrightAsync(productUrl, record, ct);
        if (result != null && HasRequiredSpecs(result))
            return result;

        var searchUrl = $"{SearchBaseUrl}/en-us/search/?q={Uri.EscapeDataString(partNo)}";
        var doc = await FetchHtmlAsync(searchUrl, ct);
        if (doc == null) return ToolSpecResult.Failed("Could not load search page");

        var foundProductUrl = TryFindProductUrl(doc, partNo);
        if (!string.IsNullOrEmpty(foundProductUrl))
        {
            result = await FetchSpecsWithPlaywrightAsync(foundProductUrl, record, ct);
            if (result != null) return result;
        }

        return ParseSpecTable(doc, record);
    }

    private static async Task<ToolSpecResult?> FetchSpecsWithPlaywrightAsync(string url, ToolRecord record, CancellationToken ct)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 20000 });
            await page.GetByText("Cutting diameter", new() { Exact = false }).WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
            try { await page.GetByRole(AriaRole.Tab, new() { Name = "Metric", Exact = true }).ClickAsync(new LocatorClickOptions { Timeout = 3000 }); await Task.Delay(800, ct); } catch { }
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
            specs = await page.EvaluateAsync<JsonElement>(@"() => {
            const body = document.body.innerText;
            const idx = body.indexOf('Product data');
            const text = idx >= 0 ? body.substring(idx) : body;
            const getVal = (label) => {
                const i = text.indexOf(label);
                if (i < 0) return null;
                const chunk = text.substring(i, i + 120);
                const mm = chunk.match(/(\d+[,.]?\d*)\s*mm/);
                if (mm) return mm[1] + ' mm';
                const num = chunk.match(/(\d{1,2})(?=\D|$)/);
                return num ? num[1] : null;
            };
            return {
                dc: getVal('Cutting diameter(DC)'),
                apmx: getVal('Depth of cut maximum(APMX)'),
                re: getVal('Corner radius(RE)'),
                zefp: getVal('Peripheral effective cutting edge count(ZEFP)'),
                oal: getVal('Overall length(OAL)') || getVal('Functional length(LF)'),
                dconms: getVal('Connection diameter machine side(DCONMS)')
            };
        }");
        }
        catch
        {
            specs = default;
        }

        if (specs.ValueKind == JsonValueKind.Object)
        {
            result.Spec1 = GetJsonString(specs, "dc") ?? "#NA";
            result.Spec2 = GetJsonString(specs, "apmx") ?? "#NA";
            result.Spec3 = GetJsonString(specs, "re") ?? "#NA";
            result.Spec4 = GetJsonString(specs, "zefp") ?? "#NA";
            result.Spec5 = GetJsonString(specs, "oal") ?? "#NA";
            result.Spec6 = GetJsonString(specs, "dconms") ?? "#NA";
        }
        else if (record.IsEndmill)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(await page.ContentAsync());
            var body = GetProductDataBody(doc);
            result.Spec1 = ExtractSandvikSpec(body, "Cutting diameter(DC)") ?? "#NA";
            result.Spec2 = ExtractSandvikSpec(body, "Depth of cut maximum(APMX)") ?? "#NA";
            result.Spec3 = ExtractSandvikSpec(body, "Corner radius(RE)") ?? "#NA";
            result.Spec4 = ExtractSandvikSpec(body, "Peripheral effective cutting edge count(ZEFP)") ?? "#NA";
            result.Spec5 = ExtractSandvikSpec(body, "Overall length(OAL)") ?? ExtractSandvikSpec(body, "Functional length(LF)") ?? "#NA";
            result.Spec6 = ExtractSandvikSpec(body, "Connection diameter machine side(DCONMS)") ?? "#NA";
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

    private static string? TryFindProductUrl(HtmlDocument doc, string toolDesc)
    {
        var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'/product') or contains(@href,'product-details')]");
        if (links == null) return null;
        var keywords = toolDesc.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var link in links.Take(30))
        {
            var href = link.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href)) continue;
            var fullUrl = href.StartsWith("http") ? href : "https://www.sandvik.coromant.com" + (href.StartsWith("/") ? href : "/" + href);
            var text = (link.InnerText + " " + href).ToUpperInvariant();
            if (keywords.Any(k => text.Contains(k)))
                return fullUrl;
        }
        var first = links.FirstOrDefault()?.GetAttributeValue("href", "");
        return string.IsNullOrEmpty(first) ? null : (first.StartsWith("http") ? first : "https://www.sandvik.coromant.com" + first);
    }

    private ToolSpecResult ParseSpecTable(HtmlDocument doc, ToolRecord record)
    {
        var result = new ToolSpecResult { Success = true };
        var body = GetProductDataBody(doc);

        if (record.IsEndmill)
        {
            result.Spec1 = ExtractSandvikSpec(body, "Cutting diameter(DC)") ?? "#NA";
            result.Spec2 = ExtractSandvikSpec(body, "Depth of cut maximum(APMX)") ?? "#NA";
            result.Spec3 = ExtractSandvikSpec(body, "Corner radius(RE)") ?? "#NA";
            result.Spec4 = ExtractSandvikSpec(body, "Peripheral effective cutting edge count(ZEFP)") ?? "#NA";
            result.Spec5 = ExtractSandvikSpec(body, "Overall length(OAL)") ?? ExtractSandvikSpec(body, "Functional length(LF)") ?? "#NA";
            result.Spec6 = ExtractSandvikSpec(body, "Connection diameter machine side(DCONMS)") ?? "#NA";
        }
        else
        {
            result.Spec1 = ExtractSandvikSpec(body, "Cutting diameter(DC)") ?? "#NA";
            result.Spec2 = ExtractSandvikSpec(body, "Usable length(LU)") ?? "#NA";
            result.Spec5 = ExtractSandvikSpec(body, "Overall length(OAL)") ?? ExtractSandvikSpec(body, "Functional length(LF)") ?? "#NA";
            result.Spec6 = ExtractSandvikSpec(body, "Connection diameter machine side(DCONMS)") ?? "#NA";
        }

        return result;
    }

    /// <summary>Get text only from Product data section to avoid header/badge garbage (New, Generic representation, Show 3D model).</summary>
    private static string GetProductDataBody(HtmlDocument doc)
    {
        var full = doc.DocumentNode.InnerText;
        var idx = full.IndexOf("Product data", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0 && full.IndexOf("Cutting diameter", idx, StringComparison.OrdinalIgnoreCase) >= 0)
            return full[idx..];
        return full;
    }

    private static string? ExtractSandvikSpec(string body, string label)
    {
        var escaped = System.Text.RegularExpressions.Regex.Escape(label.Trim());
        // Metric only: match mm values. Use full label to avoid false matches (e.g. "RE" in "representation").
        // Pattern 1: label + value with mm (e.g. "13 mm")
        // Pattern 2: label + plain integer for unitless specs (e.g. "4" for edge count)
        var m = System.Text.RegularExpressions.Regex.Match(body, escaped + @"[\s\S]{0,80}?(\d+[,.]?\d*)\s*mm|" + escaped + @"[\s\S]{0,80}?(\d{1,2})(?=\D|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var val = m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value.Trim();
            if (string.IsNullOrEmpty(val)) return null;
            var isMeasurement = m.Groups[1].Success;
            var result = NormalizeValue(isMeasurement ? val + " mm" : val);
            return result == "#NA" ? null : result;
        }
        return null;
    }
}
