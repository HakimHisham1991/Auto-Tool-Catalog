using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoToolCatalog.Models;
using AutoToolCatalog.Models.Kennametal;

namespace AutoToolCatalog.Services.Kennametal;

public partial class KennametalApiClient : IKennametalApiClient
{
    private const string KennametalSite = "https://www.kennametal.com";
    private const string CadApiBase = "https://www.product-config.net/catalog3/cad?d=kennametal";
    private const string CommerceSearchApi = $"{KennametalSite}/ws/v2/kmt/products/search";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record ResolvedProduct(string ProductId, string? ProductUrl);

    private readonly IHttpClientFactory _httpClientFactory;

    public KennametalApiClient(IHttpClientFactory httpClientFactory) =>
        _httpClientFactory = httpClientFactory;

    public async Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("KENNAMETAL");

            var productId = ExtractProductIdFromUrl(record.WebpageLink);
            string? productUrl = record.WebpageLink;

            if (string.IsNullOrWhiteSpace(productId))
            {
                var commerce = await SearchProductViaCommerceApiAsync(client, record.ToolDescription, ct);
                if (commerce != null)
                {
                    productId = commerce.ProductId;
                    productUrl = commerce.ProductUrl ?? productUrl;
                }
            }

            if (string.IsNullOrWhiteSpace(productId))
                productId = await SearchProductIdAsync(client, record.ToolDescription, ct);

            if (string.IsNullOrWhiteSpace(productId) && !string.IsNullOrWhiteSpace(record.ToolDescription))
            {
                var resolved = await KennametalBrowserApiFetcher.ResolveByPartNumberAsync(record.ToolDescription, ct);
                if (resolved != null)
                {
                    productId = resolved.ProductId;
                    productUrl = resolved.ProductUrl;
                }
            }

            if (string.IsNullOrWhiteSpace(productId))
                return ProductFetchResult.Failed("Could not resolve Kennametal product ID from link or part number");

            productUrl ??= BuildProductUrl(productId, record.WebpageLink);
            var json = await GetCadJsonAsync(client, productId, productUrl, ct);
            if (string.IsNullOrWhiteSpace(json))
                return ProductFetchResult.Failed("Kennametal CAD API returned no data");

            var cad = JsonSerializer.Deserialize<KennametalCadDto>(json, JsonOptions);
            var properties = cad == null ? new Dictionary<string, string>() : NormalizeAttributes(cad);
            if (properties.Count == 0)
                return ProductFetchResult.Failed("Invalid or empty Kennametal API response (no attributes)");

            return new ProductFetchResult
            {
                Success = true,
                ProductUrl = productUrl,
                ItemNumber = cad?.ProductId ?? productId,
                RawJson = json,
                Properties = properties
            };
        }
        catch (Exception ex)
        {
            return ProductFetchResult.Failed(ex.Message);
        }
    }

    internal static Dictionary<string, string> NormalizeAttributes(KennametalCadDto cad)
    {
        var prefix = SupplierPrefixes.GetPropertyPrefix(SupplierPrefixes.Kennametal);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var count = Math.Min(cad.Attributes.Count, cad.AttributeValues.Count);

        for (var i = 0; i < count; i++)
        {
            var attr = cad.Attributes[i];
            var value = cad.AttributeValues[i]?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var keyName = !string.IsNullOrWhiteSpace(attr.CadParameterName)
                ? attr.CadParameterName.Trim()
                : !string.IsNullOrWhiteSpace(attr.Label)
                    ? attr.Label.Trim()
                    : attr.Id?.Trim();

            if (string.IsNullOrWhiteSpace(keyName))
                continue;

            properties[$"{prefix}{SanitizeColumnName(keyName)}"] = value;
        }

        return properties;
    }

    private static string SanitizeColumnName(string name) =>
        InvalidColumnCharsRegex().Replace(name, "_").Trim('_');

    private async Task<string?> GetCadJsonAsync(HttpClient client, string productId, string referer, CancellationToken ct)
    {
        var url = $"{CadApiBase}&id={Uri.EscapeDataString(productId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<ResolvedProduct?> SearchProductViaCommerceApiAsync(
        HttpClient client, string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var trimmed = query.Trim();
        if (!LooksLikePartNumber(trimmed))
            return null;

        var searchQuery = $"{trimmed}:relevance";
        var url =
            $"{CommerceSearchApi}?query={Uri.EscapeDataString(searchQuery)}&fields=FULL&pageSize=20";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Referer", $"{KennametalSite}/us/en/home.html");

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        return PickProductFromCommerceSearch(json, trimmed);
    }

    private static ResolvedProduct? PickProductFromCommerceSearch(string json, string designation)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("products", out var products) ||
            products.ValueKind != JsonValueKind.Array)
            return null;

        var normalizedDesignation = NormalizePartToken(designation);

        foreach (var product in products.EnumerateArray())
        {
            var code = GetJsonString(product, "code");
            if (string.IsNullOrWhiteSpace(code))
                continue;

            if (!MatchesCatalogDesignation(product, designation, normalizedDesignation))
                continue;

            var relativeUrl = GetJsonString(product, "url");
            var absoluteUrl = string.IsNullOrWhiteSpace(relativeUrl)
                ? null
                : relativeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? relativeUrl
                    : $"{KennametalSite}/us/en{relativeUrl}";

            return new ResolvedProduct(code, absoluteUrl);
        }

        return null;
    }

    private static bool MatchesCatalogDesignation(
        JsonElement product, string designation, string normalizedDesignation)
    {
        foreach (var field in new[] { "catalogISO", "catalogANSI" })
        {
            var catalog = GetJsonString(product, field);
            if (string.IsNullOrWhiteSpace(catalog))
                continue;

            if (catalog.Equals(designation, StringComparison.OrdinalIgnoreCase))
                return true;

            if (NormalizePartToken(catalog) == normalizedDesignation)
                return true;
        }

        var description = GetJsonString(product, "description");
        return !string.IsNullOrWhiteSpace(description) &&
               description.Contains(designation, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetJsonString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el))
            return null;

        return el.ValueKind == JsonValueKind.String ? el.GetString()?.Trim() : null;
    }

    private static string NormalizePartToken(string value) =>
        PartTokenNormalizeRegex().Replace(value.ToUpperInvariant(), string.Empty);

    private async Task<string?> SearchProductIdAsync(HttpClient client, string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var trimmed = query.Trim();

        var fromText = ProductIdRegex().Match(trimmed);
        if (fromText.Success)
            return fromText.Groups[1].Value;

        if (!LooksLikePartNumber(trimmed))
            return null;

        var searchUrl = $"{KennametalSite}/us/en/search.html?searchTerm={Uri.EscapeDataString(trimmed)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var html = await response.Content.ReadAsStringAsync(ct);
        return PickProductIdFromHtml(html, trimmed);
    }

    private static string? PickProductIdFromHtml(string html, string designation)
    {
        var normalizedDesignation = NormalizePartToken(designation);

        foreach (Match match in ProductPageRegex().Matches(html))
        {
            var id = match.Groups[1].Value;
            var start = Math.Max(0, match.Index - 500);
            var length = Math.Min(html.Length - start, 1000);
            var window = html.AsSpan(start, length).ToString();
            if (window.Contains(designation, StringComparison.OrdinalIgnoreCase) ||
                window.Contains(normalizedDesignation, StringComparison.OrdinalIgnoreCase))
                return id;
        }

        var first = ProductPageRegex().Match(html);
        return first.Success ? first.Groups[1].Value : null;
    }

    private static bool LooksLikePartNumber(string text) =>
        PartNumberRegex().IsMatch(text);

    private static string? ExtractProductIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var match = ProductPageRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string BuildProductUrl(string productId, string? existingLink)
    {
        if (!string.IsNullOrWhiteSpace(existingLink) &&
            existingLink.Contains(productId, StringComparison.OrdinalIgnoreCase))
            return existingLink;

        return $"{KennametalSite}/us/en/products/p.product.{productId}.html";
    }

    [GeneratedRegex(@"\.(\d{5,})\.html", RegexOptions.IgnoreCase)]
    private static partial Regex ProductPageRegex();

    [GeneratedRegex(@"\b(\d{6,})\b")]
    private static partial Regex ProductIdRegex();

    [GeneratedRegex(@"[^A-Za-z0-9_]+")]
    private static partial Regex InvalidColumnCharsRegex();

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9.\-/]{4,}$", RegexOptions.IgnoreCase)]
    private static partial Regex PartNumberRegex();

    [GeneratedRegex(@"[^A-Z0-9]")]
    private static partial Regex PartTokenNormalizeRegex();
}
