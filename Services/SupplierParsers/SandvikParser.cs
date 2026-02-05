using AutoToolCatalog.Models;
using HtmlAgilityPack;

namespace AutoToolCatalog.Services.SupplierParsers;

public class SandvikParser : BaseSupplierParser
{
    public SandvikParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public override string SupplierName => "SANDVIK";
    protected override string SearchBaseUrl => "https://www.sandvik.coromant.com";

    protected override async Task<ToolSpecResult> FetchSpecsCoreAsync(ToolRecord record, CancellationToken ct)
    {
        var searchUrl = $"{SearchBaseUrl}/en-us/search/?q={Uri.EscapeDataString(record.ToolDescription)}";
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
        var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'/product/') or contains(@href,'/en-us/')]");
        if (links == null) return null;
        var first = links.FirstOrDefault()?.GetAttributeValue("href", "");
        if (string.IsNullOrEmpty(first)) return null;
        return first.StartsWith("http") ? first : "https://www.sandvik.coromant.com" + first;
    }

    private ToolSpecResult ParseSpecTable(HtmlDocument doc, ToolRecord record)
    {
        var result = new ToolSpecResult { Success = true };

        if (record.IsEndmill)
        {
            result.Spec1 = ExtractSpec(doc, new[] { "DC", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractSpec(doc, new[] { "APMX", "APmax", "cutting length" }) ?? "#NA";
            result.Spec3 = ExtractSpec(doc, new[] { "RE", "corner", "radius" }) ?? "#NA";
            result.Spec4 = ExtractSpec(doc, new[] { "ZEFP", "Z", "flute", "teeth" }) ?? "#NA";
            result.Spec5 = ExtractSpec(doc, new[] { "LF", "OAL", "length", "overall" }) ?? "#NA";
            result.Spec6 = ExtractSpec(doc, new[] { "DCONMS", "shank", "bore" }) ?? "#NA";
        }
        else
        {
            result.Spec1 = ExtractSpec(doc, new[] { "DC", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractSpec(doc, new[] { "LU", "length", "flute" }) ?? "#NA";
            result.Spec5 = ExtractSpec(doc, new[] { "OAL", "LF", "length", "overall" }) ?? "#NA";
            result.Spec6 = ExtractSpec(doc, new[] { "DCONMS", "shank", "diameter" }) ?? "#NA";
        }

        return result;
    }
}
