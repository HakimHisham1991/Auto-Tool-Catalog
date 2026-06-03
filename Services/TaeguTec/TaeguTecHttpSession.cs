using System.Net;
using System.Text.RegularExpressions;
using AutoToolCatalog.Models.TaeguTec;

namespace AutoToolCatalog.Services.TaeguTec;

/// <summary>
/// Shared HTTP session with cookie jar for the TaeguTec IMC e-catalog (ASP.NET session required).
/// NOTE: the IMC site is Cloudflare-protected; plain HTTP receives a 403 JS challenge from most IPs.
/// Use <see cref="TaeguTecBrowserbaseFetcher"/> when a Browserbase key is configured.
/// </summary>
public sealed partial class TaeguTecHttpSession : ITaeguTecItemFetcher
{
    private const string BaseUrl = "https://www.imc-companies.com/taegutec/ttkcatalog/";

    private readonly HttpClient _client;
    private readonly SemaphoreSlim _warmupLock = new(1, 1);
    private bool _sessionWarmed;

    public TaeguTecHttpSession()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
    }

    public async Task<TaeguTecItemDto?> FetchItemAsync(
        string catalogNo,
        string? knownItemUrl,
        CancellationToken ct = default)
    {
        await EnsureSessionWarmedAsync(ct);

        var fnum = ExtractQueryParam(knownItemUrl, "fnum");
        var mapp = ExtractQueryParam(knownItemUrl, "mapp");

        if (string.IsNullOrWhiteSpace(fnum))
        {
            (fnum, mapp) = await ResolveFnumAsync(catalogNo, ct);
            if (string.IsNullOrWhiteSpace(fnum))
                return null;
        }

        mapp ??= "ML";

        var itemUrl = $"{BaseUrl}Item.aspx?cat={catalogNo}&fnum={fnum}&mapp={mapp}&GFSTYP=M&srch=1";
        var html = await GetHtmlAsync(itemUrl, ct);
        if (!TaeguTecHtmlParser.LooksLikeItemPage(html))
        {
            ResetSession();
            await EnsureSessionWarmedAsync(ct);
            html = await GetHtmlAsync(itemUrl, ct);
            if (!TaeguTecHtmlParser.LooksLikeItemPage(html))
                return null;
        }

        return TaeguTecHtmlParser.ParseItemPage(html!, catalogNo);
    }

    public static string? ExtractCatalogNo(string? link, string? description)
    {
        if (!string.IsNullOrWhiteSpace(link))
        {
            var catParam = CatalogInUrlRegex().Match(link);
            if (catParam.Success)
                return catParam.Groups[1].Value;

            var urlNum = PathCatalogRegex().Match(link);
            if (urlNum.Success)
                return urlNum.Groups[1].Value;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            var descNum = DescriptionCatalogRegex().Match(description);
            if (descNum.Success)
                return descNum.Groups[1].Value;
        }

        return null;
    }

    public static string BuildItemUrl(string catalogNo, string? fnum, string? mapp) =>
        $"{BaseUrl}Item.aspx?cat={catalogNo}&fnum={fnum ?? "0"}&mapp={mapp ?? "ML"}&GFSTYP=M&srch=1";

    private async Task EnsureSessionWarmedAsync(CancellationToken ct)
    {
        if (_sessionWarmed) return;

        await _warmupLock.WaitAsync(ct);
        try
        {
            if (_sessionWarmed) return;
            await _client.GetAsync($"{BaseUrl}Index.aspx", ct);
            _sessionWarmed = true;
        }
        finally
        {
            _warmupLock.Release();
        }
    }

    private void ResetSession() => _sessionWarmed = false;

    private async Task<(string? Fnum, string? Mapp)> ResolveFnumAsync(string catalogNo, CancellationToken ct)
    {
        var searchUrl = $"{BaseUrl}search.aspx?cat={catalogNo}&stype=1&styp=E";
        var searchHtml = await GetHtmlAsync(searchUrl, ct);
        if (TaeguTecHtmlParser.TryExtractFnum(searchHtml, out var fnum, out var mapp))
            return (fnum, mapp);

        return (null, null);
    }

    private async Task<string?> GetHtmlAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractQueryParam(string? url, string name)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var match = Regex.Match(url, $@"[?&]{name}=([^&]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"[?&]cat=(\d{6,8})", RegexOptions.IgnoreCase)]
    private static partial Regex CatalogInUrlRegex();

    [GeneratedRegex(@"/(\d{6,8})(?:\.html?)?", RegexOptions.IgnoreCase)]
    private static partial Regex PathCatalogRegex();

    [GeneratedRegex(@"\b(\d{6,8})\b")]
    private static partial Regex DescriptionCatalogRegex();
}
