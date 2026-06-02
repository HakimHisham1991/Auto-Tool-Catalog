using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoToolCatalog.Models;
using AutoToolCatalog.Models.Seco;

namespace AutoToolCatalog.Services.Seco;

public partial class SecoApiClient : ISecoApiClient
{
    private const string BaseUrl = "https://www.secotools.com";
    private const string GetFullProductUrl = $"{BaseUrl}/core/api/Products/GetFullProduct";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpClientFactory;

    public SecoApiClient(IHttpClientFactory httpClientFactory) =>
        _httpClientFactory = httpClientFactory;

    public async Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default)
    {
        try
        {
            using var handler = CreateHandler();
            using var client = CreateClient(handler);

            var itemNumber = await ResolveItemNumberAsync(client, record, ct);
            string? productUrl = null;
            string? json = null;
            var statusCode = HttpStatusCode.NotFound;

            if (!string.IsNullOrWhiteSpace(itemNumber))
            {
                productUrl = $"{BaseUrl}/article/p_{itemNumber}";
                (json, statusCode) = await GetFullProductJsonAsync(client, itemNumber, productUrl, ct);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                var browser = await SecoBrowserApiFetcher.FetchAsync(
                    record.WebpageLink, record.ToolDescription, itemNumber, ct);
                if (browser != null)
                {
                    itemNumber = browser.ItemNumber;
                    productUrl = browser.ProductUrl;
                    json = browser.Json;
                }
            }

            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(itemNumber))
                return ProductFetchResult.Failed("Could not resolve SECO item number or product data from link, description, or search");

            productUrl ??= $"{BaseUrl}/article/p_{itemNumber}";

            var product = JsonSerializer.Deserialize<SecoProductDto>(json, JsonOptions);
            var properties = product == null ? new Dictionary<string, string>() : NormalizeAttributes(product);
            if (properties.Count == 0)
                return ProductFetchResult.Failed("Invalid or empty SECO API response (no attributes)");

            return new ProductFetchResult
            {
                Success = true,
                ProductUrl = productUrl,
                ItemNumber = product?.ItemNumber ?? itemNumber,
                RawJson = json,
                Properties = properties
            };
        }
        catch (Exception ex)
        {
            return ProductFetchResult.Failed(ex.Message);
        }
    }

    internal static Dictionary<string, string> NormalizeAttributes(SecoProductDto product)
    {
        var prefix = SupplierPrefixes.GetPropertyPrefix(SupplierPrefixes.Seco);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attr in product.Attributes)
        {
            var valueText = attr.ValueText;
            if (string.IsNullOrWhiteSpace(attr.Name) || string.IsNullOrWhiteSpace(valueText))
                continue;

            var value = valueText.Trim();
            if (!string.IsNullOrWhiteSpace(attr.Unit) && !value.Contains(attr.Unit, StringComparison.OrdinalIgnoreCase))
                value = $"{value} {attr.Unit}".Trim();

            if (!string.IsNullOrWhiteSpace(attr.ValueDescription) &&
                attr.ValueDescription != value &&
                !value.Contains(attr.ValueDescription, StringComparison.OrdinalIgnoreCase))
                value = $"{value} ({attr.ValueDescription.Trim()})";

            properties[$"{prefix}{attr.Name.Trim()}"] = value;
        }

        return properties;
    }

    private static HttpClientHandler CreateHandler() =>
        new() { UseCookies = true, CookieContainer = new CookieContainer(), AutomaticDecompression = DecompressionMethods.All };

    private static HttpClient CreateClient(HttpClientHandler handler)
    {
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        return client;
    }

    private async Task WarmupAsync(HttpClient client, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/");
        await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private async Task<string?> ResolveItemNumberAsync(HttpClient client, ToolRecord record, CancellationToken ct)
    {
        await WarmupAsync(client, ct);

        var fromLink = ExtractItemNumberFromUrl(record.WebpageLink);
        if (!string.IsNullOrWhiteSpace(fromLink))
            return fromLink;

        var fromDescription = ExtractItemNumberFromText(record.ToolDescription);
        if (!string.IsNullOrWhiteSpace(fromDescription))
            return fromDescription;

        var fromSearch = await SearchItemNumberAsync(client, record.ToolDescription, ct);
        if (!string.IsNullOrWhiteSpace(fromSearch))
            return fromSearch;

        return await SearchItemNumberByDesignationApiAsync(client, record.ToolDescription, ct);
    }

    private async Task<(string? Json, HttpStatusCode Status)> GetFullProductJsonAsync(
        HttpClient client, string itemNumber, string referer, CancellationToken ct)
    {
        var url = $"{GetFullProductUrl}?itemNumber={Uri.EscapeDataString(itemNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        using var response = await client.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadAsStringAsync(ct), response.StatusCode);

        return (null, response.StatusCode);
    }

    private async Task<string?> SearchItemNumberAsync(HttpClient client, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var searchUrl = $"{BaseUrl}/search?q={Uri.EscapeDataString(query.Trim())}";
        using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
        request.Headers.TryAddWithoutValidation("Referer", $"{BaseUrl}/");

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var html = await response.Content.ReadAsStringAsync(ct);
        var match = ArticlePathRegex().Match(html);
        if (match.Success)
            return match.Groups[1].Value;

        return ExtractItemNumberFromEmbeddedJson(html);
    }

    private async Task<string?> SearchItemNumberByDesignationApiAsync(HttpClient client, string designation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(designation))
            return null;

        var endpoints = new[]
        {
            $"{BaseUrl}/core/api/Search/GetSearchResult?searchText={Uri.EscapeDataString(designation)}",
            $"{BaseUrl}/core/api/GlobalSearch/GetSearchResult?searchText={Uri.EscapeDataString(designation)}",
            $"{BaseUrl}/core/api/Products/Search?searchText={Uri.EscapeDataString(designation)}"
        };

        foreach (var url in endpoints)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                request.Headers.TryAddWithoutValidation("Referer", $"{BaseUrl}/");
                request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

                using var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    continue;

                var json = await response.Content.ReadAsStringAsync(ct);
                var fromJson = ExtractItemNumberFromEmbeddedJson(json);
                if (!string.IsNullOrWhiteSpace(fromJson))
                    return fromJson;
            }
            catch
            {
                // try next endpoint
            }
        }

        return null;
    }

    private static string? ExtractItemNumberFromEmbeddedJson(string text)
    {
        var match = ItemNumberJsonRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractItemNumberFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var match = ArticlePathRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractItemNumberFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = ItemNumberRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"article/p_(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArticlePathRegex();

    [GeneratedRegex(@"\b(\d{8})\b")]
    private static partial Regex ItemNumberRegex();

    [GeneratedRegex(@"""ItemNumber""\s*:\s*""(\d{8})""", RegexOptions.IgnoreCase)]
    private static partial Regex ItemNumberJsonRegex();
}
