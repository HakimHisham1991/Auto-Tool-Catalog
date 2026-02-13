using System.Text.Json;
using AutoToolCatalog.Models;
using HtmlAgilityPack;
using Microsoft.Playwright;

namespace AutoToolCatalog.Services.SupplierParsers;

public class SecoParser : BaseSupplierParser
{
    public SecoParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public override string SupplierName => "SECO";
    protected override string SearchBaseUrl => "https://www.secotools.com";
    protected override TimeSpan PerAttemptTimeout => TimeSpan.FromSeconds(60);

    protected override async Task<ToolSpecResult> FetchSpecsCoreAsync(ToolRecord record, CancellationToken ct)
    {
        // Seco's website is JavaScript-rendered (KnockoutJS SPA).
        // HttpClient gets only the HTML skeleton with no spec data.
        // Use Playwright: homepage search → autocomplete → product page → extract specs.
        var result = await FetchSpecsWithPlaywrightAsync(record, ct);
        if (result != null && HasRequiredSpecs(result))
            return result;

        // Fallback: try HttpClient-based parsing (rarely returns data for JS-rendered pages)
        var searchUrl = $"{SearchBaseUrl}/search?q={Uri.EscapeDataString(record.ToolDescription)}";
        var doc = await FetchHtmlAsync(searchUrl, ct);
        if (doc == null) return ToolSpecResult.Failed("Could not load Seco page");

        var productUrl = TryFindProductUrl(doc, record.ToolDescription);
        if (string.IsNullOrEmpty(productUrl))
            return ParseSpecTable(doc, record);

        var productDoc = await FetchHtmlAsync(productUrl, ct);
        return productDoc != null ? ParseSpecTable(productDoc, record) : ParseSpecTable(doc, record);
    }

    private async Task<ToolSpecResult?> FetchSpecsWithPlaywrightAsync(ToolRecord record, CancellationToken ct)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();

            // Step 1: Load homepage to get session/cookies
            await page.GotoAsync(SearchBaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 20000 });

            // Step 2: Type part number in search box to trigger autocomplete
            var searchInput = page.Locator("input[type='search'], input[placeholder*='earch'], input[data-bind*='search']").First;
            await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
            await searchInput.FillAsync(record.ToolDescription);
            await Task.Delay(3000, ct); // Wait for autocomplete suggestions

            // Step 3: Find the product link from autocomplete results
            var productLink = await page.EvaluateAsync<string?>(@"(partNo) => {
                // Match links containing /article/p_ (product pages)
                const links = [...document.querySelectorAll('a')].filter(a =>
                    a.href && a.href.includes('/article/p_')
                );
                if (links.length > 0) return links[0].href;

                // Match links whose text contains part of the part number
                const prefix = partNo.substring(0, 8).toUpperCase();
                const textLinks = [...document.querySelectorAll('a')].filter(a =>
                    a.textContent.toUpperCase().includes(prefix)
                );
                return textLinks.length > 0 ? textLinks[0].href : null;
            }", record.ToolDescription);

            if (string.IsNullOrEmpty(productLink))
            {
                await page.CloseAsync();
                return null;
            }

            // Step 4: Navigate to product page
            await page.GotoAsync(productLink, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 20000 });
            await Task.Delay(2000, ct); // Wait for specs to render

            // Step 5: Extract specs from the rendered page
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
            // Seco spec table renders as tab-separated text: CODE\tDescription\tValue
            specs = await page.EvaluateAsync<JsonElement>(@"() => {
                const text = document.body.innerText;

                const getSpec = (code) => {
                    const re = new RegExp('^' + code + '\\t[^\\n]*\\t([\\d,.]+\\s*mm)', 'm');
                    const m = text.match(re);
                    return m ? m[1].trim() : null;
                };

                const getInt = (code) => {
                    const re = new RegExp('^' + code + '\\t[^\\n]*\\t(\\d+)', 'm');
                    const m = text.match(re);
                    return m ? m[1] : null;
                };

                return {
                    dc: getSpec('DC'),
                    apmxs: getSpec('APMXS') || getSpec('APMX'),
                    re: getSpec('RE'),
                    pcedc: getInt('PCEDC') || getInt('FCEDC'),
                    oal: getSpec('OAL'),
                    dmm: getSpec('DMM') || getSpec('DCONMS')
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
            result.Spec2 = GetJsonString(specs, "apmxs") ?? "#NA";  // Flute length
            result.Spec3 = GetJsonString(specs, "re") ?? "#NA";     // Corner rad
            result.Spec4 = GetJsonString(specs, "pcedc") ?? "#NA";  // Edge count
            result.Spec5 = GetJsonString(specs, "oal") ?? "#NA";    // OAL
            result.Spec6 = GetJsonString(specs, "dmm") ?? "#NA";    // Shank/bore Ø
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

    // ---- Fallback: static HTML parsing (rarely works for JS-rendered sites) ----

    private static string? TryFindProductUrl(HtmlDocument doc, string toolDesc)
    {
        var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'/article/') or contains(@href,'/product/')]");
        if (links == null) return null;
        var keywords = toolDesc.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var link in links.Take(10))
        {
            var href = link.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href)) continue;
            var fullUrl = href.StartsWith("http") ? href : "https://www.secotools.com" + href;
            var text = (link.InnerText + " " + href).ToUpperInvariant();
            if (keywords.Any(k => text.Contains(k)))
                return fullUrl;
        }
        var first = links.FirstOrDefault()?.GetAttributeValue("href", "");
        return string.IsNullOrEmpty(first) ? null : (first.StartsWith("http") ? first : "https://www.secotools.com" + first);
    }

    private ToolSpecResult ParseSpecTable(HtmlDocument doc, ToolRecord record)
    {
        var result = new ToolSpecResult { Success = true };

        if (record.IsEndmill)
        {
            result.Spec1 = ExtractSpec(doc, new[] { "DC", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractSpec(doc, new[] { "APMXS", "APMX", "APmax", "Depth of cut maximum", "cutting length" }) ?? "#NA";
            result.Spec3 = ExtractSpec(doc, new[] { "RE", "corner", "radius" }) ?? "#NA";
            result.Spec4 = ExtractSpec(doc, new[] { "PCEDC", "Z", "flute", "teeth" }) ?? "#NA";
            result.Spec5 = ExtractSpec(doc, new[] { "OAL", "Overall length", "length", "overall" }) ?? "#NA";
            result.Spec6 = ExtractSpec(doc, new[] { "DMM", "Shank diameter", "shank", "bore" }) ?? "#NA";
        }
        else
        {
            result.Spec1 = ExtractSpec(doc, new[] { "DC", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractSpec(doc, new[] { "LU", "length", "flute" }) ?? "#NA";
            result.Spec5 = ExtractSpec(doc, new[] { "OAL", "length", "overall" }) ?? "#NA";
            result.Spec6 = ExtractSpec(doc, new[] { "DCONMS", "Connection diameter machine side", "shank", "diameter" }) ?? "#NA";
        }

        return result;
    }
}
