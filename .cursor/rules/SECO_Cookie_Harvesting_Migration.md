# SECO Cookie Harvesting Migration — Cursor Agent Instructions

## Objective

Replace all Playwright/Chromium usage in the SECO pipeline with a pure `HttpClient` +
`CookieContainer` approach. After this migration **no browser process is spawned on the
server** — MonsterASP will no longer suspend the app.

The other supplier pipelines (Kennametal, Sandvik, Walter, TaeguTec) are **not touched**.

---

## Confirmed API Contract (from DevTools — do not guess)

| Field | Value |
|---|---|
| Endpoint | `https://www.secotools.com/core/api/Products/GetFullProduct` |
| Method | **POST** |
| Content-Type | `application/x-www-form-urlencoded; charset=UTF-8` |
| Auth mechanism | Session cookies only — **no CSRF token required** |
| Example product page | `https://www.secotools.com/article/p_02968233` |
| Warmup URL | `https://www.secotools.com/article/{itemId}` (visit product page first) |

### Required Request Headers (copy exactly)

```
Accept:             application/json, text/javascript, */*; q=0.01
Content-Type:       application/x-www-form-urlencoded; charset=UTF-8
X-Requested-With:  XMLHttpRequest
X-Seco-api:        (empty string — header must be present but value is empty)
Referer:           https://www.secotools.com/article/{itemId}
Origin:            https://www.secotools.com
sec-ch-ua:         "Chromium";v="148", "Microsoft Edge";v="148", "Not/A)Brand";v="99"
sec-ch-ua-mobile:  ?0
sec-ch-ua-platform: "Windows"
Sec-Fetch-Dest:    empty
Sec-Fetch-Mode:    cors
Sec-Fetch-Site:    same-origin
User-Agent:        Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36 Edg/148.0.0.0
```

### POST Body

The body is `application/x-www-form-urlencoded`. Confirm the exact field name(s) by
checking the **Payload** tab in DevTools for the same request. It will look like one of:

```
itemId=p_02968233
```
or
```
articleNumber=p_02968233
```

Check the Payload tab — do not guess the field name. The item ID format appears to be
`p_XXXXXXXX` (e.g. `p_02968233`).

### Critical Cookies

The `CookieContainer` handles all cookies automatically after the warmup GET. The two
most important are set by Azure's load balancer and must persist across requests:

- `ARRAffinity` — routes all requests to the same backend server
- `ARRAffinitySameSite` — same as above, SameSite variant

If these are missing, the session breaks mid-run. They are set automatically on the
first GET to `secotools.com` — do not manually set them.

---

## Files to Read Before Making Any Changes

```
Services/Seco/SecoHttpSession.cs
Services/Seco/SecoPlaywrightPool.cs
Services/Seco/SecoBrowserApiFetcher.cs
Services/Seco/SecoApiClient.cs
Services/Seco/SecoProductDataProvider.cs
Services/PlaywrightBootstrap.cs
Program.cs
AutoToolCatalog.csproj
```

Understand:
- Where `SecoBrowserApiFetcher` is called and under what conditions
- What `SecoPlaywrightPool` creates and disposes
- What `SecoHttpSession` currently manages
- What DI registrations exist in `Program.cs` for Playwright-related classes

---

## Step 1 — Rewrite `SecoHttpSession.cs`

Replace the current implementation with the following. Keep the class name and
namespace unchanged so existing call sites compile without modification.

```csharp
public class SecoHttpSession : IDisposable
{
    private readonly HttpClient _client;
    private readonly CookieContainer _cookieContainer;
    private bool _warmedUp = false;
    private readonly SemaphoreSlim _warmupLock = new(1, 1);

    public SecoHttpSession()
    {
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

        // Base headers present on every request
        _client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/148.0.0.0 Safari/537.36 Edg/148.0.0.0");
        _client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        _client.DefaultRequestHeaders.Add("sec-ch-ua",
            "\"Chromium\";v=\"148\", \"Microsoft Edge\";v=\"148\", \"Not/A)Brand\";v=\"99\"");
        _client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
        _client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
    }

    public HttpClient Client => _client;
    public CookieContainer Cookies => _cookieContainer;

    /// <summary>
    /// Visits the product page for the given itemId to establish session cookies
    /// (TrackSessionId, ARRAffinity, ARRAffinitySameSite, etc.).
    /// Safe to call concurrently — only warms up once per instance lifetime.
    /// </summary>
    public async Task EnsureWarmedUpAsync(string itemId)
    {
        if (_warmedUp) return;

        await _warmupLock.WaitAsync();
        try
        {
            if (_warmedUp) return;

            var warmupUrl = $"https://www.secotools.com/article/{itemId}";
            var request = new HttpRequestMessage(HttpMethod.Get, warmupUrl);
            request.Headers.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.Add("Sec-Fetch-Dest", "document");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Site", "none");

            var response = await _client.SendAsync(request);
            // 200 or redirect — either way cookies are set
            _warmedUp = response.IsSuccessStatusCode || (int)response.StatusCode < 400;
        }
        finally
        {
            _warmupLock.Release();
        }
    }

    /// <summary>
    /// Forces re-warmup on next call. Use this if a 401/403 is received mid-run
    /// (session cookie expired).
    /// </summary>
    public void ResetWarmup()
    {
        _warmedUp = false;
    }

    public void Dispose()
    {
        _client.Dispose();
        _warmupLock.Dispose();
    }
}
```

**Registration:** Confirm `SecoHttpSession` is registered as a **singleton** in
`Program.cs`. It must be singleton — the `CookieContainer` must persist across all
row fetches in a session, just like the old shared Chromium instance.

---

## Step 2 — Rewrite `GetFullProductAsync` in `SecoApiClient.cs`

Replace the Playwright-based fetch with this HTTP implementation. The method signature
must stay the same.

```csharp
public async Task<SecoProductDto?> GetFullProductAsync(string itemId)
{
    // Step 1: warm up session cookies by visiting the product page
    await _session.EnsureWarmedUpAsync(itemId);

    // Step 2: POST to the confirmed API endpoint
    const string apiUrl = "https://www.secotools.com/core/api/Products/GetFullProduct";

    // Confirm exact field name from DevTools Payload tab
    // It is one of: itemId=, articleNumber=, productId=
    // Replace "itemId" below with whatever DevTools shows
    var formData = new Dictionary<string, string>
    {
        { "itemId", itemId }    // <-- VERIFY THIS FIELD NAME IN DEVTOOLS
    };

    var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
    {
        Content = new FormUrlEncodedContent(formData)
    };

    // Per-request headers (not set on DefaultRequestHeaders to avoid conflicts)
    request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
    request.Headers.Add("X-Requested-With", "XMLHttpRequest");
    request.Headers.Add("X-Seco-api", string.Empty);   // must be present, value empty
    request.Headers.Add("Referer", $"https://www.secotools.com/article/{itemId}");
    request.Headers.Add("Origin", "https://www.secotools.com");
    request.Headers.Add("Sec-Fetch-Dest", "empty");
    request.Headers.Add("Sec-Fetch-Mode", "cors");
    request.Headers.Add("Sec-Fetch-Site", "same-origin");
    request.Headers.Add("Cache-Control", "no-cache");
    request.Headers.Add("Pragma", "no-cache");

    var response = await _session.Client.SendAsync(request);

    if (!response.IsSuccessStatusCode)
    {
        // On 401/403: reset warmup and retry once (handles session expiry)
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _session.ResetWarmup();
            await _session.EnsureWarmedUpAsync(itemId);

            request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new FormUrlEncodedContent(formData)
            };
            // Re-add headers (HttpRequestMessage is not reusable)
            request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("X-Seco-api", string.Empty);
            request.Headers.Add("Referer", $"https://www.secotools.com/article/{itemId}");
            request.Headers.Add("Origin", "https://www.secotools.com");
            request.Headers.Add("Sec-Fetch-Dest", "empty");
            request.Headers.Add("Sec-Fetch-Mode", "cors");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");

            response = await _session.Client.SendAsync(request);
        }

        if (!response.IsSuccessStatusCode)
        {
            // Log: $"SECO API returned {response.StatusCode} for {itemId}"
            return null;
        }
    }

    var json = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<SecoProductDto>(json, _jsonOptions);
}
```

---

## Step 3 — Replace `SecoBrowserApiFetcher` Search with HTTP

Find the SECO search API endpoint using DevTools: type a tool designation in the SECO
site search box and watch the Network > Fetch/XHR tab. The autocomplete/search
endpoint will be visible. It typically looks like:

```
GET https://www.secotools.com/core/api/Products/Search?query=XXXXX&...
```
or
```
POST https://www.secotools.com/core/api/search/...
```

**Capture this URL from DevTools**, then add a `SearchForItemIdAsync` method to
`SecoApiClient`:

```csharp
public async Task<string?> SearchForItemIdAsync(string toolDescription)
{
    await _session.EnsureWarmedUpAsync("p_02968233"); // any valid product for warmup

    // REPLACE THIS URL WITH THE ACTUAL SEARCH ENDPOINT FROM DEVTOOLS
    var encoded = Uri.EscapeDataString(toolDescription);
    var searchUrl = $"https://www.secotools.com/core/api/Products/Search?query={encoded}&pageSize=1";

    var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
    request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
    request.Headers.Add("X-Requested-With", "XMLHttpRequest");
    request.Headers.Add("X-Seco-api", string.Empty);
    request.Headers.Add("Referer", "https://www.secotools.com/");
    request.Headers.Add("Sec-Fetch-Dest", "empty");
    request.Headers.Add("Sec-Fetch-Mode", "cors");
    request.Headers.Add("Sec-Fetch-Site", "same-origin");

    var response = await _session.Client.SendAsync(request);
    if (!response.IsSuccessStatusCode) return null;

    var json = await response.Content.ReadAsStringAsync();

    // Parse first result's item ID — adjust JSON path to match actual response
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    // Common patterns — try each until one works:
    // root.GetProperty("results")[0].GetProperty("itemId")
    // root.GetProperty("products")[0].GetProperty("articleNumber")
    // root.GetProperty("hits")[0].GetProperty("objectID")
    return null; // replace with actual parse once JSON structure is confirmed
}
```

Replace the Playwright call in `SecoBrowserApiFetcher` (or its call site) with
`SearchForItemIdAsync`.

---

## Step 4 — Delete `SecoPlaywrightPool.cs`

- Remove all references to `SecoPlaywrightPool` from `SecoApiClient.cs` and anywhere else.
- Delete the file `Services/Seco/SecoPlaywrightPool.cs`.
- Remove its DI registration from `Program.cs`.

---

## Step 5 — Delete or Stub `SecoBrowserApiFetcher.cs`

Once `SearchForItemIdAsync` is implemented in `SecoApiClient`:

- If `SecoBrowserApiFetcher` is only used from one call site — delete the file and
  replace the call with `_secoApiClient.SearchForItemIdAsync(...)`.
- If it is injected broadly via DI — keep the file but gut the implementation body
  and throw `NotSupportedException` so missed call sites surface immediately during testing.
- Remove its DI registration from `Program.cs`.

---

## Step 6 — Guard `PlaywrightBootstrap.cs`

Add an env var guard so startup never launches Node.exe on the server:

```csharp
public static async Task InstallAsync()
{
    // Set DISABLE_PLAYWRIGHT_INSTALL=true in MonsterASP environment variables
    if (Environment.GetEnvironmentVariable("DISABLE_PLAYWRIGHT_INSTALL") == "true")
    {
        return; // No local Chromium install — using HttpClient
    }
    // ... existing install code unchanged
}
```

Do not delete this file — useful for local dev/debugging.

---

## Step 7 — Update `publish-for-ftp.ps1`

After `dotnet publish`, remove Playwright binaries from the output:

```powershell
$playwrightDir = Join-Path $publishPath ".playwright"
if (Test-Path $playwrightDir) {
    Remove-Item -Recurse -Force $playwrightDir
    Write-Host "Removed .playwright binaries (HttpClient mode — not needed)" -ForegroundColor Green
}
```

---

## Step 8 — Remove Playwright NuGet Package

In `AutoToolCatalog.csproj`, remove:

```xml
<PackageReference Include="Microsoft.Playwright" Version="..." />
```

Run `dotnet build` immediately after. If it fails with `Microsoft.Playwright` namespace
errors, a reference was missed — find it, remove it, rebuild. The package must be gone
before deploying to MonsterASP.

---

## Step 9 — MonsterASP Environment Variable

In the MonsterASP control panel, set:

```
DISABLE_PLAYWRIGHT_INSTALL = true
```

---

## Step 10 — Local Testing Protocol

Test in this exact order before deploying:

**Test A — Known item ID with product page link**
Upload one SECO row with a full `secotools.com/article/p_XXXXXXXX` link. Confirm
`SECO_DC`, `SECO_APMX`, etc. are populated. Check logs — must see 200 on both the
warmup GET and the POST.

**Test B — Item ID only, no link**
Remove the link. Pipeline resolves via item ID directly. Confirm same output as Test A.

**Test C — Designation-only row**
Row has only `Tool Description`, no link, no item ID. Exercises `SearchForItemIdAsync`.
Confirm either successful resolution or a clean "not found" error — no exception, no crash.

**Test D — 5 concurrent SECO rows**
Upload 5 SECO rows simultaneously. All 5 should complete. The shared `SecoHttpSession`
singleton and its single `CookieContainer` must handle concurrency — `HttpClient` is
thread-safe, `CookieContainer` is thread-safe.

**If Test A returns 405:** The POST body field name is wrong — recheck DevTools Payload tab.
**If Test A returns 403:** Add `Cache-Control: no-cache` and `Pragma: no-cache` to the request and retry.
**If Test D fails partway:** Session cookie expired mid-run — the `ResetWarmup()` + retry
logic in `GetFullProductAsync` should catch this. If not, add logging to confirm which
status code is returned on failure.

---

## What Does NOT Change

- `SecoGlobalIdStore.cs`
- `SecoProductDataProvider.cs`
- `CatalogRepository.cs`
- `ScraperService.cs`
- All other supplier pipelines
- SignalR progress reporting
- Excel import/export
- `Program.cs` DI except for Playwright-related removals

---

## Summary of All File Changes

| File | Action |
|---|---|
| `Services/Seco/SecoHttpSession.cs` | **Rewrite** — `CookieContainer`, `EnsureWarmedUpAsync(itemId)`, `ResetWarmup()` |
| `Services/Seco/SecoApiClient.cs` | **Rewrite** `GetFullProductAsync` — POST with form body; add `SearchForItemIdAsync` |
| `Services/Seco/SecoPlaywrightPool.cs` | **Delete** |
| `Services/Seco/SecoBrowserApiFetcher.cs` | **Delete or stub** |
| `Services/PlaywrightBootstrap.cs` | **Add** `DISABLE_PLAYWRIGHT_INSTALL` guard |
| `scripts/publish-for-ftp.ps1` | **Add** `.playwright` folder cleanup |
| `Program.cs` | **Remove** DI registrations for deleted classes |
| `AutoToolCatalog.csproj` | **Remove** `Microsoft.Playwright` package reference |
| MonsterASP env vars | **Add** `DISABLE_PLAYWRIGHT_INSTALL=true` |

---

## Success Criteria

1. `dotnet build` — zero errors, no Playwright namespaces anywhere.
2. Publish output has no `.playwright` folder.
3. All 4 local tests (A–D) pass.
4. Deployed to MonsterASP — site loads, SECO rows process successfully.
5. No suspension email from MonsterASP about Node.exe.
