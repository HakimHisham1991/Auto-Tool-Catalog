using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoToolCatalog.Services.Seco;

/// <summary>
/// Shared HTTP client with cookie jar for SECO product API calls (no browser required).
/// </summary>
public sealed partial class SecoHttpSession : IDisposable
{
    private const string BaseUrl = "https://www.secotools.com";
    private const string GetFullProductUrl = $"{BaseUrl}/core/api/Products/GetFullProduct";

    private readonly HttpClient _client;
    private readonly CookieContainer _cookieContainer;
    private readonly string _market;
    private readonly string _language;
    private readonly SemaphoreSlim _warmupLock = new(1, 1);
    private bool _warmedUp;

    public SecoHttpSession(IConfiguration configuration)
    {
        _market = configuration["Seco:Market"] ?? "MY";
        _language = configuration["Seco:Language"] ?? "en-GB";

        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = _cookieContainer,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip
                                   | DecompressionMethods.Deflate
                                   | DecompressionMethods.Brotli
        };

        _client = new HttpClient(handler);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/148.0.0.0 Safari/537.36 Edg/148.0.0.0");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua",
            "\"Chromium\";v=\"148\", \"Microsoft Edge\";v=\"148\", \"Not/A)Brand\";v=\"99\"");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
    }

    public HttpClient Client => _client;
    public CookieContainer Cookies => _cookieContainer;

    public async Task<string?> ResolveItemNumberAsync(
        string? webpageLink, string? toolDescription, CancellationToken ct)
    {
        var fromLink = ExtractItemNumberFromUrl(webpageLink);
        if (!string.IsNullOrWhiteSpace(fromLink))
            return fromLink;

        var fromDescription = ExtractItemNumberFromText(toolDescription);
        if (!string.IsNullOrWhiteSpace(fromDescription))
            return fromDescription;

        if (string.IsNullOrWhiteSpace(toolDescription))
            return null;

        await EnsureWarmedUpAsync(fromLink ?? fromDescription ?? "02968233", ct);

        var fromDesignationSearch = await SearchByDesignationAsync(toolDescription, ct);
        if (!string.IsNullOrWhiteSpace(fromDesignationSearch))
            return fromDesignationSearch;

        return await SearchItemNumberAsync(toolDescription, ct);
    }

    /// <summary>
    /// Visits the product page to establish session cookies (TrackSessionId, ARRAffinity, etc.).
    /// </summary>
    public async Task EnsureWarmedUpAsync(string itemNumber, CancellationToken ct = default)
    {
        if (_warmedUp) return;

        await _warmupLock.WaitAsync(ct);
        try
        {
            if (_warmedUp) return;

            var articlePath = ArticlePathForItemNumber(itemNumber);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{articlePath}");
            request.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            _warmedUp = response.IsSuccessStatusCode || (int)response.StatusCode < 400;
        }
        finally
        {
            _warmupLock.Release();
        }
    }

    public void ResetWarmup() => _warmedUp = false;

    public async Task<string?> FetchGetFullProductJsonAsync(string itemNumber, CancellationToken ct = default)
    {
        await EnsureWarmedUpAsync(itemNumber, ct);

        var response = await SendGetFullProductAsync(itemNumber, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ResetWarmup();
            await EnsureWarmedUpAsync(itemNumber, ct);
            response.Dispose();
            response = await SendGetFullProductAsync(itemNumber, ct);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            return json.Contains("Attributes", StringComparison.OrdinalIgnoreCase) ? json : null;
        }
    }

    private async Task<HttpResponseMessage> SendGetFullProductAsync(string itemNumber, CancellationToken ct)
    {
        var formData = new Dictionary<string, string>
        {
            ["itemNumber"] = NormalizeItemNumber(itemNumber),
            ["market"] = _market,
            ["language"] = _language
        };

        var articlePath = ArticlePathForItemNumber(itemNumber);
        using var request = new HttpRequestMessage(HttpMethod.Post, GetFullProductUrl)
        {
            Content = new FormUrlEncodedContent(formData)
        };

        AddApiHeaders(request, $"{BaseUrl}{articlePath}");
        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static void AddApiHeaders(HttpRequestMessage request, string referer)
    {
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("X-Seco-api", string.Empty);
        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("Origin", BaseUrl);
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
    }

    private async Task<string?> SearchItemNumberAsync(string query, CancellationToken ct)
    {
        var searchUrl = $"{BaseUrl}/search?q={Uri.EscapeDataString(query.Trim())}";
        using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
        request.Headers.TryAddWithoutValidation("Referer", $"{BaseUrl}/");

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var html = await response.Content.ReadAsStringAsync(ct);
        var match = ArticlePathRegex().Match(html);
        if (match.Success)
            return match.Groups[1].Value;

        return ExtractItemNumberFromEmbeddedJson(html);
    }

    private async Task<string?> SearchByDesignationAsync(string designation, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(designation.Trim());
        var url = $"{BaseUrl}/core/api/Products/SearchProducedProducts?searchTerms={encoded}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
        request.Headers.TryAddWithoutValidation("Referer", $"{BaseUrl}/");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("X-Seco-api", string.Empty);
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        return PickItemNumberFromSearchResults(json, designation);
    }

    internal static string? PickItemNumberFromSearchResults(string json, string designation)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var normalized = NormalizeDesignation(designation);
            string? first = null;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("ItemNumber", out var numberProp) ||
                    numberProp.ValueKind != JsonValueKind.String)
                    continue;

                var itemNumber = numberProp.GetString();
                if (string.IsNullOrWhiteSpace(itemNumber))
                    continue;

                first ??= NormalizeItemNumber(itemNumber);

                foreach (var nameProp in new[] { "Designation", "DesignationAnsi" })
                {
                    if (item.TryGetProperty(nameProp, out var designationProp) &&
                        designationProp.ValueKind == JsonValueKind.String &&
                        NormalizeDesignation(designationProp.GetString()!) == normalized)
                        return NormalizeItemNumber(itemNumber);
                }
            }

            return first;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeDesignation(string value) =>
        value.Trim().ToUpperInvariant();

    private static string? ExtractItemNumberFromEmbeddedJson(string text)
    {
        var match = ItemNumberJsonRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? ExtractItemNumberFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var match = ArticlePathRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractItemNumberFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = ItemNumberRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string NormalizeItemNumber(string itemNumber)
    {
        var trimmed = itemNumber.Trim();
        if (trimmed.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
            return trimmed[2..];
        return trimmed;
    }

    private static string ArticlePathForItemNumber(string itemNumber) =>
        $"/article/p_{NormalizeItemNumber(itemNumber)}";

    public void Dispose()
    {
        _client.Dispose();
        _warmupLock.Dispose();
    }

    [GeneratedRegex(@"article/p_(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArticlePathRegex();

    [GeneratedRegex(@"\b(\d{8})\b")]
    private static partial Regex ItemNumberRegex();

    [GeneratedRegex(@"""ItemNumber""\s*:\s*""(\d{8})""", RegexOptions.IgnoreCase)]
    private static partial Regex ItemNumberJsonRegex();
}
