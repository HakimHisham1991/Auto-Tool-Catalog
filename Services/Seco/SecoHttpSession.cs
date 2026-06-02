using System.Net;
using System.Text.RegularExpressions;

namespace AutoToolCatalog.Services.Seco;

/// <summary>
/// Shared HTTP client with cookie jar for SECO item-number resolution (warmup once per process).
/// </summary>
internal sealed partial class SecoHttpSession
{
    private const string BaseUrl = "https://www.secotools.com";

    private static readonly Lazy<SecoHttpSession> Instance = new(() => new SecoHttpSession());
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _warmupLock = new(1, 1);
    private bool _warmedUp;

    private SecoHttpSession()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All
        };
        _client = new HttpClient(handler);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
    }

    public static SecoHttpSession Default => Instance.Value;

    public async Task<string?> ResolveItemNumberAsync(
        string? webpageLink, string? toolDescription, CancellationToken ct)
    {
        await EnsureWarmAsync(ct);

        var fromLink = ExtractItemNumberFromUrl(webpageLink);
        if (!string.IsNullOrWhiteSpace(fromLink))
            return fromLink;

        var fromDescription = ExtractItemNumberFromText(toolDescription);
        if (!string.IsNullOrWhiteSpace(fromDescription))
            return fromDescription;

        if (string.IsNullOrWhiteSpace(toolDescription))
            return null;

        var fromSearch = await SearchItemNumberAsync(toolDescription, ct);
        if (!string.IsNullOrWhiteSpace(fromSearch))
            return fromSearch;

        return await SearchItemNumberByDesignationApiAsync(toolDescription, ct);
    }

    private async Task EnsureWarmAsync(CancellationToken ct)
    {
        if (_warmedUp) return;
        await _warmupLock.WaitAsync(ct);
        try
        {
            if (_warmedUp) return;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/");
            await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            _warmedUp = true;
        }
        finally
        {
            _warmupLock.Release();
        }
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

    private async Task<string?> SearchItemNumberByDesignationApiAsync(string designation, CancellationToken ct)
    {
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

                using var response = await _client.SendAsync(request, ct);
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

    [GeneratedRegex(@"article/p_(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArticlePathRegex();

    [GeneratedRegex(@"\b(\d{8})\b")]
    private static partial Regex ItemNumberRegex();

    [GeneratedRegex(@"""ItemNumber""\s*:\s*""(\d{8})""", RegexOptions.IgnoreCase)]
    private static partial Regex ItemNumberJsonRegex();
}
