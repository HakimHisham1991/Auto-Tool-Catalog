using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoToolCatalog.Models;
using AutoToolCatalog.Models.Sandvik;
using Microsoft.AspNetCore.WebUtilities;

namespace AutoToolCatalog.Services.Sandvik;

public partial class SandvikApiClient : ISandvikApiClient
{
    private const string SandvikSite = "https://www.sandvik.coromant.com";
    private const string Language = "en-gb";
    private const string Country = "gb";
    private const string UnitOfMeasurement = "Metric";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpClientFactory;

    public SandvikApiClient(IHttpClientFactory httpClientFactory) =>
        _httpClientFactory = httpClientFactory;

    public async Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SANDVIK");

            var materialId = ExtractMaterialIdFromUrl(record.WebpageLink);
            string? orderCode = ExtractOrderCodeFromUrl(record.WebpageLink);

            if (string.IsNullOrWhiteSpace(materialId))
            {
                var match = await SearchMaterialAsync(client, record.ToolDescription, ct);
                if (match == null)
                    return ProductFetchResult.Failed("Could not resolve Sandvik material ID from link or tool description");

                materialId = match.Id;
                orderCode ??= match.Title;
            }

            if (string.IsNullOrWhiteSpace(materialId))
                return ProductFetchResult.Failed("Could not resolve Sandvik material ID from link or tool description");

            var productUrl = BuildProductUrl(materialId, orderCode, record.WebpageLink);
            var json = await GetProductJsonAsync(client, materialId, productUrl, ct);
            if (string.IsNullOrWhiteSpace(json))
                return ProductFetchResult.Failed("Sandvik product API returned no data");

            var response = JsonSerializer.Deserialize<SandvikProductResponseDto>(json, JsonOptions);
            var properties = response == null
                ? new Dictionary<string, string>()
                : NormalizeProduct(response);
            if (properties.Count == 0)
                return ProductFetchResult.Failed("Invalid or empty Sandvik API response (no detail properties)");

            orderCode ??= TryGetProductString(response!.Product, "ORDCODE");
            productUrl = BuildProductUrl(materialId, orderCode, record.WebpageLink);

            return new ProductFetchResult
            {
                Success = true,
                ProductUrl = productUrl,
                ItemNumber = materialId,
                RawJson = json,
                Properties = properties
            };
        }
        catch (Exception ex)
        {
            return ProductFetchResult.Failed(ex.Message);
        }
    }

    internal static Dictionary<string, string> NormalizeProduct(SandvikProductResponseDto response)
    {
        var prefix = SupplierPrefixes.GetPropertyPrefix(SupplierPrefixes.Sandvik);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in response.Properties.Where(p => p.IsDetails && !string.IsNullOrWhiteSpace(p.Title)))
        {
            if (!TryGetProductValue(response.Product, definition.Title!, out var rawValue))
                continue;

            var value = FormatValue(rawValue);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!string.IsNullOrWhiteSpace(definition.Unit) &&
                !value.Contains(definition.Unit, StringComparison.OrdinalIgnoreCase))
                value = $"{value} {definition.Unit}".Trim();

            properties[$"{prefix}{definition.Title!.Trim()}"] = value;
        }

        return properties;
    }

    private async Task<SandvikAutocompleteItemDto?> SearchMaterialAsync(
        HttpClient client, string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var trimmed = query.Trim();
        var url =
            $"{SandvikSite}/api/productsearch/getautocompleteitems" +
            $"?query={Uri.EscapeDataString(trimmed)}" +
            "&queryContext=CoromantGB" +
            "&autocompleteType=coromantproductsearch" +
            "&objectType=AllWithPermutations" +
            "&itemsToReturn=10";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Referer", $"{SandvikSite}/{Language}/");

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        var items = JsonSerializer.Deserialize<List<SandvikAutocompleteItemDto>>(json, JsonOptions);
        if (items == null || items.Count == 0)
            return null;

        return items.FirstOrDefault(i =>
                   string.Equals(i.Type, "ordcode", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(i.MatchType, "exact", StringComparison.OrdinalIgnoreCase))
               ?? items.FirstOrDefault(i => string.Equals(i.Type, "ordcode", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> GetProductJsonAsync(
        HttpClient client, string materialId, string referer, CancellationToken ct)
    {
        var url =
            $"{SandvikSite}/api/productsearch/product" +
            $"?id={Uri.EscapeDataString(materialId)}" +
            $"&unitOfMeasurement={UnitOfMeasurement}" +
            $"&language={Language}" +
            $"&country={Country}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Referer", referer);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsStringAsync(ct);
    }

    private static bool TryGetProductValue(JsonElement product, string title, out JsonElement value)
    {
        if (product.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in product.EnumerateObject())
        {
            if (!string.Equals(property.Name, title, StringComparison.OrdinalIgnoreCase))
                continue;

            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static string? TryGetProductString(JsonElement product, string title) =>
        TryGetProductValue(product, title, out var value) ? FormatValue(value) : null;

    private static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(FormatValue).Where(v => !string.IsNullOrWhiteSpace(v))),
        JsonValueKind.Number => value.TryGetInt64(out var whole)
            ? whole.ToString(CultureInfo.InvariantCulture)
            : value.GetDouble().ToString(CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static string? ExtractMaterialIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var match = MaterialIdRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractOrderCodeFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!query.TryGetValue("c", out var values))
            return null;

        var orderCode = values.ToString();
        return string.IsNullOrWhiteSpace(orderCode) ? null : orderCode.Trim();
    }

    private static string BuildProductUrl(string materialId, string? orderCode, string? existingLink)
    {
        if (!string.IsNullOrWhiteSpace(existingLink) &&
            existingLink.Contains(materialId, StringComparison.OrdinalIgnoreCase))
            return existingLink;

        var code = string.IsNullOrWhiteSpace(orderCode) ? materialId : orderCode;
        return $"{SandvikSite}/{Language}/product-details?c={Uri.EscapeDataString(code)}&m={Uri.EscapeDataString(materialId)}";
    }

    [GeneratedRegex(@"[?&]m=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MaterialIdRegex();
}
