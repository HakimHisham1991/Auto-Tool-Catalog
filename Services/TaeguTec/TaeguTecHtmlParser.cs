using System.Net;
using System.Text.RegularExpressions;
using AutoToolCatalog.Models.TaeguTec;
using HtmlAgilityPack;

namespace AutoToolCatalog.Services.TaeguTec;

/// <summary>
/// Shared HTML parsing for TaeguTec item/search pages. Used by both the HTTP and Browserbase fetchers.
/// </summary>
public static partial class TaeguTecHtmlParser
{
    public static bool LooksLikeItemPage(string? html) =>
        !string.IsNullOrEmpty(html) &&
        html.Contains("content_gvwItemParameters", StringComparison.OrdinalIgnoreCase);

    public static bool LooksLikeCloudflareChallenge(string? html) =>
        !string.IsNullOrEmpty(html) &&
        (html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
         html.Contains("cf_chl", StringComparison.OrdinalIgnoreCase) ||
         html.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase));

    public static bool TryExtractFnum(string? searchHtml, out string fnum, out string mapp)
    {
        fnum = string.Empty;
        mapp = "ML";
        if (string.IsNullOrEmpty(searchHtml))
            return false;

        var match = ItemLinkRegex().Match(searchHtml);
        if (match.Success)
        {
            fnum = match.Groups[2].Value;
            mapp = match.Groups[3].Value;
            return true;
        }

        var fallback = FallbackFnumRegex().Match(searchHtml);
        if (fallback.Success)
        {
            fnum = fallback.Groups[2].Value;
            mapp = "ML";
            return true;
        }

        return false;
    }

    public static TaeguTecItemDto ParseItemPage(string html, string catalogNo)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var dto = new TaeguTecItemDto
        {
            CatalogNo = catalogNo,
            ItemDesignation = GetText(doc, "content_lblItemDesignation"),
            FamilyDesignation = GetText(doc, "content_hlFamilyName"),
            FamilyDescription = GetText(doc, "content_lblFamilyDesc"),
            ItemsPerPackage = StripLabel(GetText(doc, "content_lblPackagePerItem")),
            FamilyRemarks = GetText(doc, "content_lblFamilyRemarks"),
            ImageUrl2D = doc.GetElementbyId("content_d2ImageReg")?.GetAttributeValue("value", null),
            Grade = doc.GetElementbyId("content_gvwItemData")
                ?.SelectSingleNode(".//a[contains(@href,'Grade.aspx')]")
                ?.InnerText.Trim() ?? ""
        };

        var paramTable = doc.GetElementbyId("content_gvwItemParameters");
        if (paramTable == null)
            return dto;

        var rows = paramTable.SelectNodes(".//tr");
        if (rows == null || rows.Count < 2)
            return dto;

        var headerNodes = rows[0].SelectNodes(".//th") ?? new HtmlNodeCollection(null);
        var headers = headerNodes
            .Select(th => new
            {
                Code = CleanCell(th.InnerText),
                IsVisible = th.GetAttributeValue("class", "").Contains("ItemGridParams1", StringComparison.Ordinal)
            })
            .ToList();

        var valueNodes = rows[1].SelectNodes(".//td") ?? new HtmlNodeCollection(null);
        var values = valueNodes
            .Select(td => new
            {
                Value = CleanCell(td.InnerText).TrimEnd('~').Trim(),
                IsVisible = td.GetAttributeValue("class", "").Contains("ItemGridParamsValue1", StringComparison.Ordinal)
            })
            .ToList();

        for (var i = 0; i < Math.Min(headers.Count, values.Count); i++)
        {
            if (!headers[i].IsVisible || !values[i].IsVisible)
                continue;

            var code = headers[i].Code;
            var value = values[i].Value;
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(value))
                dto.Parameters[code] = value;
        }

        return dto;
    }

    private static string GetText(HtmlDocument doc, string id) =>
        CleanCell(doc.GetElementbyId(id)?.InnerText ?? "");

    private static string StripLabel(string value)
    {
        var idx = value.IndexOf(':');
        return idx >= 0 ? value[(idx + 1)..].Trim() : value;
    }

    private static string CleanCell(string raw) =>
        WebUtility.HtmlDecode(raw).Trim();

    [GeneratedRegex(@"Item\.aspx\?cat=(\d+)&fnum=(\d+)&mapp=(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex ItemLinkRegex();

    [GeneratedRegex(@"cat=(\d+)&fnum=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex FallbackFnumRegex();
}
