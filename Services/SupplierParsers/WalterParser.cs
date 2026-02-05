using AutoToolCatalog.Models;
using HtmlAgilityPack;

namespace AutoToolCatalog.Services.SupplierParsers;

public class WalterParser : BaseSupplierParser
{
    public WalterParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public override string SupplierName => "WALTER";
    protected override string SearchBaseUrl => "https://www.walter-tools.com";

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
        return first.StartsWith("http") ? first : "https://www.walter-tools.com" + first;
    }

    private ToolSpecResult ParseSpecTable(HtmlDocument doc, ToolRecord record)
    {
        var result = new ToolSpecResult { Success = true };

        if (record.IsEndmill)
        {
            result.Spec1 = ExtractSpec(doc, new[] { "Dc", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractSpec(doc, new[] { "Lc", "cutting length" }) ?? "#NA";
            result.Spec3 = ExtractSpec(doc, new[] { "R", "corner", "radius" }) ?? "#NA";
            result.Spec4 = ExtractSpec(doc, new[] { "Z", "flute", "teeth" }) ?? "#NA";
            result.Spec5 = ExtractSpec(doc, new[] { "l1", "length", "overall" }) ?? "#NA";
            result.Spec6 = ExtractSpec(doc, new[] { "d1", "Connection diameter", "Shank diameter (h6)", "shank", "bore" }) ?? "#NA";
        }
        else
        {
            result.Spec1 = ExtractSpec(doc, new[] { "Dc", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractSpec(doc, new[] { "LC", "length", "flute" }) ?? "#NA";
            result.Spec5 = ExtractSpec(doc, new[] { "l1", "length", "overall" }) ?? "#NA";
            result.Spec6 = ExtractSpec(doc, new[] { "d1", "Shank diameter (h6)", "Connection diameter", "shank", "diameter" }) ?? "#NA";
        }

        return result;
    }
}
