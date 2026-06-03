using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AutoToolCatalog.Models.TaeguTec;
using Microsoft.Extensions.Logging;

namespace AutoToolCatalog.Services.TaeguTec;

/// <summary>
/// Fetches TaeguTec item pages through a Browserbase cloud browser via raw Chrome DevTools Protocol
/// over a WebSocket. The real browser passes Cloudflare's JS challenge, and because the browser runs
/// in Browserbase's cloud, no Chromium/Playwright driver is needed on the server (MonsterASP-safe).
/// </summary>
public sealed class TaeguTecBrowserbaseFetcher : ITaeguTecItemFetcher
{
    private const string BaseUrl = "https://www.imc-companies.com/taegutec/ttkcatalog/";
    private const string SessionsApi = "https://api.browserbase.com/v1/sessions";

    // Browserbase enforces a max concurrent-session limit per plan (free = 3). We gate session
    // creation so the parallel scraper never exceeds it, and retry with backoff on 429.
    private const int MaxCreateAttempts = 6;

    private readonly string _apiKey;
    private readonly string? _projectId;
    private readonly ILogger<TaeguTecBrowserbaseFetcher>? _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly SemaphoreSlim _sessionGate;

    public TaeguTecBrowserbaseFetcher(
        string apiKey,
        string? projectId,
        ILogger<TaeguTecBrowserbaseFetcher>? logger = null,
        int maxConcurrentSessions = 2)
    {
        _apiKey = apiKey;
        _projectId = projectId;
        _logger = logger;
        _sessionGate = new SemaphoreSlim(Math.Max(1, maxConcurrentSessions));
    }

    public async Task<TaeguTecItemDto?> FetchItemAsync(string catalogNo, string? knownItemUrl, CancellationToken ct = default)
    {
        await _sessionGate.WaitAsync(ct);
        BrowserbaseSession? session = null;
        try
        {
            session = await CreateSessionAsync(ct);
            if (session is null)
                return null;

            using var cdp = new CdpConnection(_logger);
            await cdp.ConnectAsync(session.ConnectUrl, ct);

            // Warm the session and clear Cloudflare on the home page first.
            await cdp.NavigateAndWaitAsync($"{BaseUrl}Index.aspx",
                html => !TaeguTecHtmlParser.LooksLikeCloudflareChallenge(html),
                TimeSpan.FromSeconds(30), ct);

            var fnum = ExtractQueryParam(knownItemUrl, "fnum");
            var mapp = ExtractQueryParam(knownItemUrl, "mapp") ?? "ML";

            if (string.IsNullOrWhiteSpace(fnum))
            {
                var searchHtml = await cdp.NavigateAndWaitAsync(
                    $"{BaseUrl}search.aspx?cat={catalogNo}&stype=1&styp=E",
                    html => !TaeguTecHtmlParser.LooksLikeCloudflareChallenge(html),
                    TimeSpan.FromSeconds(30), ct);

                if (!TaeguTecHtmlParser.TryExtractFnum(searchHtml, out fnum, out mapp))
                {
                    _logger?.LogWarning("TaeguTec Browserbase: no fnum found in search results for cat={Catalog}", catalogNo);
                    return null;
                }
            }

            var itemUrl = $"{BaseUrl}Item.aspx?cat={catalogNo}&fnum={fnum}&mapp={mapp}&GFSTYP=M&srch=1";
            var itemHtml = await cdp.NavigateAndWaitAsync(itemUrl,
                TaeguTecHtmlParser.LooksLikeItemPage,
                TimeSpan.FromSeconds(35), ct);

            if (!TaeguTecHtmlParser.LooksLikeItemPage(itemHtml))
            {
                _logger?.LogWarning("TaeguTec Browserbase: item page did not load for cat={Catalog}", catalogNo);
                return null;
            }

            return TaeguTecHtmlParser.ParseItemPage(itemHtml!, catalogNo);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "TaeguTec Browserbase fetch failed for cat={Catalog}", catalogNo);
            return null;
        }
        finally
        {
            if (session is not null)
                await ReleaseSessionAsync(session.Id, ct);
            _sessionGate.Release();
        }
    }

    private async Task<BrowserbaseSession?> CreateSessionAsync(CancellationToken ct)
    {
        // NOTE: proxies/captcha solving require a paid Browserbase plan. A plain cloud browser still
        // clears Cloudflare's managed JS challenge on its own, so we keep the session config minimal.
        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(_projectId))
            payload["projectId"] = _projectId;
        var json = JsonSerializer.Serialize(payload);

        for (var attempt = 1; attempt <= MaxCreateAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SessionsApi)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("x-bb-api-key", _apiKey);

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var connectUrl = root.TryGetProperty("connectUrl", out var cuEl) ? cuEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(connectUrl))
                {
                    _logger?.LogError("Browserbase session response missing id/connectUrl: {Body}", body);
                    return null;
                }
                return new BrowserbaseSession(id, connectUrl);
            }

            // 429 = concurrent-session limit; a sibling session should free up shortly. Back off and retry.
            if ((int)response.StatusCode == 429 && attempt < MaxCreateAttempts)
            {
                var delay = GetRetryDelay(response, attempt);
                _logger?.LogWarning(
                    "Browserbase 429 (concurrency limit), retry {Attempt}/{Max} in {Delay}s",
                    attempt, MaxCreateAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                continue;
            }

            _logger?.LogError("Browserbase session create failed: {Status} {Body}", (int)response.StatusCode, body);
            return null;
        }

        return null;
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;

        // Exponential backoff with a cap: 3s, 6s, 12s, 24s, 30s...
        var seconds = Math.Min(30, 3 * Math.Pow(2, attempt - 1));
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task ReleaseSessionAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var payload = new Dictionary<string, object?> { ["status"] = "REQUEST_RELEASE" };
            if (!string.IsNullOrWhiteSpace(_projectId))
                payload["projectId"] = _projectId;

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{SessionsApi}/{sessionId}")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("x-bb-api-key", _apiKey);
            using var _ = await _http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Browserbase session release failed (non-fatal) for {SessionId}", sessionId);
        }
    }

    private static string? ExtractQueryParam(string? url, string name)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            url, $@"[?&]{name}=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private sealed record BrowserbaseSession(string Id, string ConnectUrl);

    /// <summary>
    /// Minimal Chrome DevTools Protocol client over a WebSocket. Attaches to the default page target
    /// and supports navigation + outerHTML extraction without any browser-automation library.
    /// </summary>
    private sealed class CdpConnection : IDisposable
    {
        private readonly ClientWebSocket _ws = new();
        private readonly ILogger? _logger;
        private int _nextId;
        private string? _sessionId;

        public CdpConnection(ILogger? logger) => _logger = logger;

        public async Task ConnectAsync(string connectUrl, CancellationToken ct)
        {
            await _ws.ConnectAsync(new Uri(connectUrl), ct);

            var targets = await SendAsync("Target.getTargets", null, null, ct);
            string? targetId = null;
            if (targets.TryGetProperty("targetInfos", out var infos))
            {
                foreach (var info in infos.EnumerateArray())
                {
                    if (info.TryGetProperty("type", out var t) && t.GetString() == "page")
                    {
                        targetId = info.GetProperty("targetId").GetString();
                        break;
                    }
                }
            }

            if (targetId is null)
            {
                var created = await SendAsync("Target.createTarget",
                    new Dictionary<string, object?> { ["url"] = "about:blank" }, null, ct);
                targetId = created.GetProperty("targetId").GetString();
            }

            var attached = await SendAsync("Target.attachToTarget",
                new Dictionary<string, object?> { ["targetId"] = targetId, ["flatten"] = true }, null, ct);
            _sessionId = attached.GetProperty("sessionId").GetString();

            await SendAsync("Page.enable", null, _sessionId, ct);
            await SendAsync("Runtime.enable", null, _sessionId, ct);
        }

        public async Task<string?> NavigateAndWaitAsync(
            string url, Func<string?, bool> isReady, TimeSpan timeout, CancellationToken ct)
        {
            await SendAsync("Page.navigate",
                new Dictionary<string, object?> { ["url"] = url }, _sessionId, ct);

            var deadline = DateTime.UtcNow + timeout;
            string? html = null;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1500, ct);
                html = await GetOuterHtmlAsync(ct);
                if (isReady(html))
                    return html;
            }
            return html;
        }

        private async Task<string?> GetOuterHtmlAsync(CancellationToken ct)
        {
            var result = await SendAsync("Runtime.evaluate",
                new Dictionary<string, object?>
                {
                    ["expression"] = "document.documentElement.outerHTML",
                    ["returnByValue"] = true
                }, _sessionId, ct);

            if (result.TryGetProperty("result", out var inner) &&
                inner.TryGetProperty("value", out var val) &&
                val.ValueKind == JsonValueKind.String)
                return val.GetString();

            return null;
        }

        private async Task<JsonElement> SendAsync(
            string method, Dictionary<string, object?>? parameters, string? sessionId, CancellationToken ct)
        {
            var id = ++_nextId;
            var msg = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters ?? new Dictionary<string, object?>()
            };
            if (sessionId is not null)
                msg["sessionId"] = sessionId;

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);

            while (true)
            {
                var text = await ReceiveAsync(ct);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl) && idEl.GetInt32() == id)
                {
                    if (root.TryGetProperty("error", out var err))
                        throw new InvalidOperationException($"CDP {method} error: {err}");
                    return root.TryGetProperty("result", out var res) ? res.Clone() : default;
                }
                // Otherwise it's an event or another command's reply; keep reading.
            }
        }

        private async Task<string> ReceiveAsync(CancellationToken ct)
        {
            var buffer = new byte[16 * 1024];
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException("CDP WebSocket closed by server");
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        public void Dispose()
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { /* best effort */ }
            _ws.Dispose();
        }
    }
}
