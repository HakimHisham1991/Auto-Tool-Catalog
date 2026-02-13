using AutoToolCatalog.Models;
using HtmlAgilityPack;

namespace AutoToolCatalog.Services.SupplierParsers;

public class KennametalParser : BaseSupplierParser
{
    public KennametalParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public override string SupplierName => "KENNAMETAL";
    protected override string SearchBaseUrl => "https://www.kennametal.com";

    protected override async Task<ToolSpecResult> FetchSpecsCoreAsync(ToolRecord record, CancellationToken ct)
    {
        var partNo = record.ToolDescription.Trim();
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

    private static string? TryDirectProductUrl(string partNo)
    {
        var directUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["H1TE4RA0400N006HBR025M"] = "https://www.kennametal.com/us/en/products/p.harvi-i-te-radiused-4-flutes-necked-weldon-shank-metric.6767970.html"
        };
        return directUrls.TryGetValue(partNo, out var url) ? url : null;
    }

    private static bool HasRequiredSpecs(ToolSpecResult r) =>
        (r.Spec2 != "#NA" && !string.IsNullOrEmpty(r.Spec2)) ||
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

        if (record.IsEndmill)
        {
            result.Spec1 = ExtractKennametalSpec(doc, "[D1] Effective Cutting Diameter") ?? ExtractSpec(doc, new[] { "D1", "D", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractKennametalSpec(doc, "[AP1MAX] 1st Maximum Cutting Depth") ?? ExtractSpec(doc, new[] { "AP1MAX", "AP1 max" }) ?? "#NA";
            result.Spec3 = ExtractKennametalSpec(doc, "[Re] Corner Radius") ?? ExtractSpec(doc, new[] { "Re", "corner", "radius" }) ?? "#NA";
            result.Spec4 = ExtractKennametalSpec(doc, "[Z] Number of Flutes") ?? ExtractSpec(doc, new[] { "Z", "Number of Flutes" }) ?? "#NA";
            result.Spec5 = ExtractKennametalSpec(doc, "[L] Overall Length") ?? ExtractSpec(doc, new[] { "Overall Length", "L", "l1" }) ?? "#NA";
            result.Spec6 = ExtractKennametalSpec(doc, "[D] Adapter / Shank / Bore Diameter") ?? ExtractSpec(doc, new[] { "Adapter / Shank / Bore Diameter", "Shank" }) ?? "#NA";
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
