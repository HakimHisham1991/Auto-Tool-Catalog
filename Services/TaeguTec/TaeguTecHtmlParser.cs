using System.Net;
using System.Text.RegularExpressions;
using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.TaeguTec;

public sealed record TaeguTecParsedItem(
    string? CatalogId,
    string? ItemDesignation,
    string? FamilyDesignation,
    string? FamilyDescription,
    IReadOnlyDictionary<string, string> Properties);

public static partial class TaeguTecHtmlParser
{
    private static readonly string[] SkipSpecHeaders =
    [
        "catalog no", "grade", "alternative", "files package", "gisc", "gtc", "dxf",
        "add to favorites", "properties file", "add to assembly", "light", "detailed"
    ];

    public static TaeguTecParsedItem Parse(string html)
    {
        var decoded = WebUtility.HtmlDecode(html);
        var catalogId = ExtractCatalogIdFromHtml(decoded);
        var itemDesignation = MatchGroup(decoded, ItemDesignationRegex())
                              ?? MatchGroup(decoded, ItemDesignationPlainRegex());
        var familyDesignation = MatchGroup(decoded, FamilyDesignationRegex());
        var familyDescription = MatchGroup(decoded, FamilyDescriptionRegex());

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var prefix = SupplierPrefixes.GetPropertyPrefix(SupplierPrefixes.TaeguTec);

        if (!string.IsNullOrWhiteSpace(catalogId))
            properties[$"{prefix}CATALOG_NO"] = catalogId;
        if (!string.IsNullOrWhiteSpace(itemDesignation))
            properties[$"{prefix}ITEM_DESIGNATION"] = itemDesignation.Trim();
        if (!string.IsNullOrWhiteSpace(familyDesignation))
            properties[$"{prefix}FAMILY"] = familyDesignation.Trim();
        if (!string.IsNullOrWhiteSpace(familyDescription))
            properties[$"{prefix}FAMILY_DESC"] = familyDescription.Trim();

        foreach (var (key, value) in ExtractSpecificationTable(decoded))
            properties[$"{prefix}{SanitizeKey(key)}"] = value;

        return new TaeguTecParsedItem(catalogId, itemDesignation, familyDesignation, familyDescription, properties);
    }

    public static string? ExtractCatalogIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var match = CatalogIdInUrlRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? ExtractCatalogIdFromHtml(string html) =>
        CatalogIdInUrlRegex().Match(html).Success
            ? CatalogIdInUrlRegex().Match(html).Groups[1].Value
            : MatchGroup(html, GicatRegex());

    private static IEnumerable<(string Key, string Value)> ExtractSpecificationTable(string html)
    {
        foreach (Match tableMatch in TableRegex().Matches(html))
        {
            var tableHtml = tableMatch.Groups[1].Value;
            var rows = RowRegex().Matches(tableHtml).Select(m => m.Groups[1].Value).ToList();
            if (rows.Count < 2)
                continue;

            for (var i = 0; i < rows.Count - 1; i++)
            {
                var headers = ExtractCells(rows[i]);
                var values = ExtractCells(rows[i + 1]);
                if (headers.Count < 3 || values.Count < 3)
                    continue;
                if (!LooksLikeSpecHeaderRow(headers))
                    continue;

                var count = Math.Min(headers.Count, values.Count);
                for (var c = 0; c < count; c++)
                {
                    var header = headers[c];
                    var value = CleanCell(values[c]);
                    if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(value))
                        continue;
                    yield return (header, value);
                }

                yield break;
            }
        }
    }

    private static bool LooksLikeSpecHeaderRow(IReadOnlyList<string> headers)
    {
        if (headers.Any(h => SkipSpecHeaders.Any(s => h.Contains(s, StringComparison.OrdinalIgnoreCase))))
            return false;

        var shortHeaders = headers.Count(h =>
            h.Length is >= 2 and <= 8 && !h.Contains(' ', StringComparison.Ordinal));
        return shortHeaders >= 3;
    }

    private static List<string> ExtractCells(string rowHtml)
    {
        var cells = new List<string>();
        foreach (Match cell in CellRegex().Matches(rowHtml))
            cells.Add(CleanCell(cell.Groups[1].Value));
        return cells;
    }

    private static string CleanCell(string raw)
    {
        var text = WebUtility.HtmlDecode(StripTags(raw)).Trim();
        text = text.TrimEnd('~').Trim();
        return Regex.Replace(text, @"\s+", " ");
    }

    private static string StripTags(string html) =>
        TagRegex().Replace(html, " ");

    private static string? MatchGroup(string html, Regex regex)
    {
        var match = regex.Match(html);
        return match.Success ? CleanCell(match.Groups[1].Value) : null;
    }

    private static string SanitizeKey(string name) =>
        InvalidColumnCharsRegex().Replace(name.Trim().ToUpperInvariant(), "_").Trim('_');

    [GeneratedRegex(@"<table[^>]*>(.*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RowRegex();

    [GeneratedRegex(@"<t[hd][^>]*>(.*?)</t[hd]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CellRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[?&]cat=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CatalogIdInUrlRegex();

    [GeneratedRegex(@"Item\s*Designation\s*:?\s*</[^>]+>\s*([^<]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ItemDesignationRegex();

    [GeneratedRegex(@"Item\s*Designation\s*:?\s*([^<|\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ItemDesignationPlainRegex();

    [GeneratedRegex(@"Family\s*Designation\s*:?\s*([^<|]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FamilyDesignationRegex();

    [GeneratedRegex(@"Family\s*Designation\s*:[^|]*\|\s*([^<]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FamilyDescriptionRegex();

    [GeneratedRegex(@"\bGICAT\b[^0-9]*(\d{5,})", RegexOptions.IgnoreCase)]
    private static partial Regex GicatRegex();

    [GeneratedRegex(@"[^A-Z0-9_]+")]
    private static partial Regex InvalidColumnCharsRegex();
}
