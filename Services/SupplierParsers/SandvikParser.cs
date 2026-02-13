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

        // Sandvik product page is client-side rendered (Angular); HttpClient gets only skeleton HTML.
        // Use Playwright to render the page and extract specs.
        var productDoc = await FetchHtmlWithPlaywrightAsync(productUrl, ct);
        if (productDoc != null)
        {
            var result = ParseSpecTable(productDoc, record);
            if (HasRequiredSpecs(result)) return result;
        }

        // Fallback: try search page, then product link (search may be server-rendered)
        var searchUrl = $"{SearchBaseUrl}/en-us/search/?q={Uri.EscapeDataString(partNo)}";
        var doc = await FetchHtmlAsync(searchUrl, ct);
        if (doc == null) return ToolSpecResult.Failed("Could not load search page");

        var foundProductUrl = TryFindProductUrl(doc, partNo);
        if (!string.IsNullOrEmpty(foundProductUrl))
        {
            productDoc = await FetchHtmlWithPlaywrightAsync(foundProductUrl, ct);
            if (productDoc != null)
                return ParseSpecTable(productDoc, record);
        }

        return ParseSpecTable(doc, record);
    }

    private static async Task<HtmlDocument?> FetchHtmlWithPlaywrightAsync(string url, CancellationToken ct)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 20000 });
            // Wait for product data section (label appears after Angular/JS loads)
            await page.GetByText("Cutting diameter", new() { Exact = false }).WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
            // Force metric view: click Metric tab so we only extract mm values (not inch)
            try { await page.GetByRole(AriaRole.Tab, new() { Name = "Metric", Exact = true }).ClickAsync(new LocatorClickOptions { Timeout = 3000 }); await Task.Delay(500, ct); } catch { /* Inch may already be active or tab name differs */ }
            var html = await page.ContentAsync();
            await page.CloseAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }
        catch (Exception)
        {
            return null;
        }
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

        if (record.IsEndmill)
        {
            result.Spec1 = ExtractSandvikSpec(doc, "Cutting diameter(DC)") ?? ExtractSpec(doc, new[] { "DC", "diameter" }, metricOnly: true) ?? "#NA";
            result.Spec2 = ExtractSandvikSpec(doc, "Depth of cut maximum(APMX)") ?? ExtractSpec(doc, new[] { "APMX", "APmax" }, metricOnly: true) ?? "#NA";
            result.Spec3 = ExtractSandvikSpec(doc, "Corner radius(RE)") ?? ExtractSpec(doc, new[] { "RE", "corner" }, metricOnly: true) ?? "#NA";
            result.Spec4 = ExtractSandvikSpec(doc, "Peripheral effective cutting edge count(ZEFP)") ?? ExtractSpec(doc, new[] { "ZEFP", "Z", "flute" }, metricOnly: true) ?? "#NA";
            result.Spec5 = ExtractSandvikSpec(doc, "Overall length(OAL)") ?? ExtractSandvikSpec(doc, "Functional length(LF)") ?? ExtractSpec(doc, new[] { "OAL", "LF", "length" }, metricOnly: true) ?? "#NA";
            result.Spec6 = ExtractSandvikSpec(doc, "Connection diameter machine side(DCONMS)") ?? ExtractSpec(doc, new[] { "DCONMS", "Connection diameter" }, metricOnly: true) ?? "#NA";
        }
        else
        {
            result.Spec1 = ExtractSandvikSpec(doc, "Cutting diameter(DC)") ?? ExtractSpec(doc, new[] { "DC", "diameter" }, metricOnly: true) ?? "#NA";
            result.Spec2 = ExtractSandvikSpec(doc, "Usable length(LU)") ?? ExtractSpec(doc, new[] { "LU", "length", "flute" }, metricOnly: true) ?? "#NA";
            result.Spec5 = ExtractSandvikSpec(doc, "Overall length(OAL)") ?? ExtractSandvikSpec(doc, "Functional length(LF)") ?? ExtractSpec(doc, new[] { "OAL", "LF", "length" }, metricOnly: true) ?? "#NA";
            result.Spec6 = ExtractSandvikSpec(doc, "Connection diameter machine side(DCONMS)") ?? ExtractSpec(doc, new[] { "DCONMS", "Connection diameter" }, metricOnly: true) ?? "#NA";
        }

        return result;
    }

    private static string? ExtractSandvikSpec(HtmlDocument doc, string label)
    {
        var body = doc.DocumentNode.InnerText;
        var escaped = System.Text.RegularExpressions.Regex.Escape(label.Trim());
        // Metric only: match mm values only. Inch values -> #NA.
        // Pattern 1: label + value with mm (e.g. "13 mm") - metric only
        // Pattern 2: label + plain integer (e.g. "4" for edge count; unitless)
        var m = System.Text.RegularExpressions.Regex.Match(body, escaped + @"[\s\S]{0,80}?(\d+[,.]?\d*)\s*mm|" + escaped + @"[\s\S]{0,80}?(\d{1,2})(?=\D|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var val = m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value.Trim();
            if (string.IsNullOrEmpty(val)) return null;
            var isMeasurement = m.Groups[1].Success;
            return NormalizeValue(isMeasurement ? val + " mm" : val);
        }
        return null;
    }
}
