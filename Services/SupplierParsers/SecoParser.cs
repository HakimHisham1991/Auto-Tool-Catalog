using AutoToolCatalog.Models;
using HtmlAgilityPack;

namespace AutoToolCatalog.Services.SupplierParsers;

public class SecoParser : BaseSupplierParser
{
    public SecoParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public override string SupplierName => "SECO";
    protected override string SearchBaseUrl => "https://www.secotools.com";

    protected override async Task<ToolSpecResult> FetchSpecsCoreAsync(ToolRecord record, CancellationToken ct)
    {
        var searchUrl = $"{SearchBaseUrl}/search?q={Uri.EscapeDataString(record.ToolDescription)}";
        var doc = await FetchHtmlAsync(searchUrl, ct);
        if (doc == null) return ToolSpecResult.Failed("Could not load search page");

        var productUrl = TryFindProductUrl(doc, record.ToolDescription);
        if (string.IsNullOrEmpty(productUrl))
            return BuildResultFromSearchPage(doc, record);

        var productDoc = await FetchHtmlAsync(productUrl, ct);
        if (productDoc == null) return BuildResultFromSearchPage(doc, record);

        return ParseSpecTable(productDoc, record);
    }

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
            result.Spec2 = ExtractSpec(doc, new[] { "APMX", "APmax", "cutting length" }) ?? "#NA";
            result.Spec3 = ExtractSpec(doc, new[] { "RE", "corner", "radius" }) ?? "#NA";
            result.Spec4 = ExtractSpec(doc, new[] { "PCEDC", "Z", "flute", "teeth" }) ?? "#NA";
            result.Spec5 = ExtractSpec(doc, new[] { "OAL", "length", "overall" }) ?? "#NA";
            result.Spec6 = ExtractSpec(doc, new[] { "DMM", "shank", "bore" }) ?? "#NA";
        }
        else
        {
            result.Spec1 = ExtractSpec(doc, new[] { "DC", "diameter" }) ?? "#NA";
            result.Spec2 = ExtractSpec(doc, new[] { "LU", "length", "flute" }) ?? "#NA";
            result.Spec5 = ExtractSpec(doc, new[] { "OAL", "length", "overall" }) ?? "#NA";
            result.Spec6 = ExtractSpec(doc, new[] { "DCONMS", "shank", "diameter" }) ?? "#NA";
        }

        return result;
    }

    private ToolSpecResult BuildResultFromSearchPage(HtmlDocument doc, ToolRecord record)
    {
        return ParseSpecTable(doc, record);
    }
}