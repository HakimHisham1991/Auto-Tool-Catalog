using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.TaeguTec;

public partial class TaeguTecApiClient : ITaeguTecApiClient
{
    private const string CatalogBase = "https://www.imc-companies.com/TaeguTec/ttkCatalog/";

    private readonly IHttpClientFactory _httpClientFactory;

    public TaeguTecApiClient(IHttpClientFactory httpClientFactory) =>
        _httpClientFactory = httpClientFactory;

    public async Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default)
    {
        try
        {
            var catalogId = TaeguTecHtmlParser.ExtractCatalogIdFromUrl(record.WebpageLink);
            string? html = null;
            string? productUrl = record.WebpageLink;

            if (!string.IsNullOrWhiteSpace(catalogId))
            {
                productUrl = BuildItemUrl(catalogId, record.WebpageLink);
                html = await TryGetHtmlAsync(productUrl, ct);
                html ??= (await TaeguTecBrowserFetcher.FetchItemByCatalogIdAsync(catalogId, ct))?.Html;
            }

            if (string.IsNullOrWhiteSpace(html) && !string.IsNullOrWhiteSpace(record.ToolDescription))
            {
                var searchUrl =
                    $"{CatalogBase}search.aspx?searchText={Uri.EscapeDataString(record.ToolDescription.Trim())}";
                html = await TryGetHtmlAsync(searchUrl, ct);

                if (html != null)
                {
                    var fromSearch = PickCatalogLink(html, record.ToolDescription);
                    if (fromSearch != null)
                    {
                        catalogId = fromSearch.Value.CatalogId;
                        productUrl = fromSearch.Value.Url;
                        html = await TryGetHtmlAsync(productUrl, ct)
                               ?? (await TaeguTecBrowserFetcher.FetchItemByCatalogIdAsync(catalogId, ct))?.Html;
                    }
                }

                if (string.IsNullOrWhiteSpace(html))
                {
                    var browser = await TaeguTecBrowserFetcher.SearchAndFetchAsync(record.ToolDescription, ct);
                    if (browser != null)
                    {
                        html = browser.Html;
                        catalogId = browser.CatalogId;
                        productUrl = browser.ProductUrl;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(html))
                return ProductFetchResult.Failed(
                    "Could not load TaeguTec catalog page (site may block automated access; try adding a product Link with cat= catalog number)");

            var parsed = TaeguTecHtmlParser.Parse(html);
            catalogId ??= parsed.CatalogId;

            if (parsed.Properties.Count == 0)
                return ProductFetchResult.Failed("TaeguTec page loaded but no specifications were found in HTML");

            productUrl ??= catalogId != null ? BuildItemUrl(catalogId, null) : null;

            return new ProductFetchResult
            {
                Success = true,
                ProductUrl = productUrl,
                ItemNumber = catalogId ?? parsed.ItemDesignation,
                RawJson = JsonSerializer.Serialize(parsed.Properties),
                Properties = new Dictionary<string, string>(parsed.Properties, StringComparer.OrdinalIgnoreCase)
            };
        }
        catch (Exception ex)
        {
            return ProductFetchResult.Failed(ex.Message);
        }
    }

    private async Task<string?> TryGetHtmlAsync(string url, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TAEGUTEC");
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            var html = await response.Content.ReadAsStringAsync(ct);
            if (html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase))
                return null;

            var isItemPage = url.Contains("item.aspx", StringComparison.OrdinalIgnoreCase);
            if (isItemPage &&
                !html.Contains("Item Designation", StringComparison.OrdinalIgnoreCase) &&
                !html.Contains("Family Designation", StringComparison.OrdinalIgnoreCase))
                return null;

            return html;
        }
        catch
        {
            return null;
        }
    }

    private static (string CatalogId, string Url)? PickCatalogLink(string searchHtml, string designation)
    {
        var normalized = NormalizeToken(designation);
        foreach (Match match in ItemLinkRegex().Matches(searchHtml))
        {
            var href = match.Groups[1].Value;
            var text = NormalizeToken(WebUtility.HtmlDecode(match.Groups[2].Value));
            var catMatch = CatalogIdInUrlRegex().Match(href);
            if (!catMatch.Success)
                continue;

            if (text.Contains(normalized, StringComparison.Ordinal) ||
                normalized.Contains(text, StringComparison.Ordinal))
                return (catMatch.Groups[1].Value, ToAbsoluteUrl(href));
        }

        var first = ItemLinkRegex().Match(searchHtml);
        if (!first.Success)
            return null;
        var cat = CatalogIdInUrlRegex().Match(first.Groups[1].Value);
        return cat.Success
            ? (cat.Groups[1].Value, ToAbsoluteUrl(first.Groups[1].Value))
            : null;
    }

    private static string BuildItemUrl(string catalogId, string? existingLink)
    {
        if (!string.IsNullOrWhiteSpace(existingLink) &&
            existingLink.Contains("item.aspx", StringComparison.OrdinalIgnoreCase))
            return existingLink;

        return $"{CatalogBase}item.aspx?cat={catalogId}&isoD=1";
    }

    private static string ToAbsoluteUrl(string href)
    {
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return href;
        var path = href.StartsWith('/') ? href : "/TaeguTec/ttkCatalog/" + href.TrimStart('/');
        return "https://www.imc-companies.com" + path;
    }

    private static string NormalizeToken(string value) =>
        Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);

    [GeneratedRegex(@"[?&]cat=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CatalogIdInUrlRegex();

    [GeneratedRegex(@"href=[""']([^""']*item\.aspx\?cat=\d+[^""']*)[""'][^>]*>([^<]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ItemLinkRegex();
}
