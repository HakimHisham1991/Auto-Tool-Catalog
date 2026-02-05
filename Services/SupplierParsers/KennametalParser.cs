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
        var searchUrl = $"{SearchBaseUrl}/us/en/search.html?q={Uri.EscapeDataString(record.ToolDescription)}";
        var doc = await FetchHtmlAsync(searchUrl, ct);
        if (doc == null) return ToolSpecResult.Failed("Could not load search page");

        var productUrl = TryFindProductUrl(doc);
        if (!string.IsNullOrEmpty(productUrl))
        {
            var productDoc = await FetchHtmlAsync(productUrl, ct);
            if (productDoc != null)
                return ParseSpecTable(productDoc, record);
        }

        return ParseSpecTable(doc, record);
    }

    private static string? TryFindProductUrl(HtmlDocument doc)
    {
        var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'/product/') or contains(@href,'/us/en/')]");
        if (links == null) return null;
        var first = links.FirstOrDefault()?.GetAttributeValue("href", "");
        if (string.IsNullOrEmpty(first)) return null;
        return first.StartsWith("http") ? first : "https://www.kennametal.com" + first;
    }

    private ToolSpecResult ParseSpecTable(HtmlDocument doc, ToolRecord record)
    {
        var result = new ToolSpecResult { Success = true };

        if (record.IsEndmill)
        {
            result.Spec1 = ExtractSpec(doc, new[] { "D1", "D", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractSpec(doc, new[] { "AP1MAX", "APmax", "cutting length" }) ?? "#NA";
            result.Spec3 = ExtractSpec(doc, new[] { "Re", "corner", "radius" }) ?? "#NA";
            result.Spec4 = ExtractSpec(doc, new[] { "Z", "flute", "teeth" }) ?? "#NA";
            result.Spec5 = ExtractSpec(doc, new[] { "l1", "length", "overall" }) ?? "#NA";
            result.Spec6 = ExtractSpec(doc, new[] { "Adapter / Shank / Bore Diameter", "Adapter", "Shank", "Bore Diameter", "D", "d1" }) ?? "#NA";
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
}
