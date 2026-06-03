# TaeguTec HTML Catalog Integration — Cursor Agent Implementation Guide

**Project:** `Auto-Tool-Catalog` (HakimHisham1991/Auto-Tool-Catalog)  
**Supplier prefix:** `TAEG_`  
**Pattern to follow:** Walter pipeline (HTTP-only, no Playwright, no native deps)

---

## 1. Context & Architecture Summary

This project is a .NET 10 ASP.NET Core Razor Pages app that enriches tooling Excel databases with supplier specs. Each supplier is handled by a `IProductDataProvider` implementation. The app already supports SECO, Kennametal, Sandvik, and Walter.

You are adding **TaeguTec** as a fifth supplier.

Key conventions in the existing codebase:
- Each supplier lives in `Services/{Supplier}/` with two files: `{Supplier}ApiClient.cs` and `{Supplier}ProductDataProvider.cs`
- DTOs live in `Models/{Supplier}/`
- The supplier constant and column prefix are registered in `Models/SupplierPrefixes.cs`
- DI wiring happens in `Program.cs`
- `ScraperService.cs` auto-discovers new property columns — no changes needed there
- Column names follow the pattern `{PREFIX}_{ISO13399_PARAM}` e.g. `TAEG_DC`, `TAEG_OAL`

**Critical MonsterASP constraint:** No Playwright/Chromium, no native DLLs. HTTP-only. The TaeguTec site does NOT have Cloudflare protection — plain `HttpClient` with a `CookieContainer` works.

---

## 2. How the TaeguTec Site Works (Required Reading)

**Base URL:** `https://www.imc-companies.com/taegutec/ttkcatalog/`

### The session problem
`Item.aspx?cat=6254778` requires a live ASP.NET `ASP.NET_SessionId` cookie. Without one, the server silently redirects to `Search.aspx` (generic listing). You must warm the session first.

### The `fnum` problem  
The full item URL requires both `cat` (catalog number) and `fnum` (family number):
```
Item.aspx?cat=6254778&fnum=11154&mapp=ML&GFSTYP=M&srch=1
```
`fnum` and `mapp` are NOT in the Excel link column — they must be resolved from a search first.

### The two-step fetch flow
```
Step 1: GET Index.aspx          → warms ASP.NET session cookie
Step 2: GET search.aspx?cat=X   → parse fnum + mapp from result links
Step 3: GET Item.aspx?cat=X&fnum=Y&mapp=Z&GFSTYP=M&srch=1  → parse specs
```

### If the Excel Link column already has the full URL
If the link looks like `...Item.aspx?cat=6254778&fnum=11154&mapp=ML...`, extract `fnum` and `mapp` from the URL directly and skip Step 2.

### Search URL format
```
https://www.imc-companies.com/taegutec/ttkcatalog/search.aspx?cat={catalogNo}&stype=1&styp=E
```

### Extracting fnum from search results
The search result HTML contains links like:
```html
<a href="Item.aspx?cat=6254778&fnum=11154&mapp=ML&GFSTYP=M">
```
Use regex: `Item\.aspx\?cat={catalogNo}&fnum=(\d+)&mapp=(\w+)`

---

## 3. HTML Parsing — Verified Table Structure

The parameters are in a `<table id="content_gvwItemParameters">`.

### Header row (`<th>` elements)
Each `<th>` has a `title` attribute with the **full parameter name** and the cell text is the **ISO13399 code**. Use the `title` for human-readable names and the `InnerText` (trimmed) for the column key.

Hidden columns use class `gridItemHidden` — **skip them**.  
Visible columns use class `ItemGridParams1` — **include these**.

### Data row (`<td>` elements)
Visible value cells use class `ItemGridParamsValue1`.  
Hidden cells use class `gridItemHidden` — **skip them**.  
Cell values may have trailing `~` characters — trim them.

### Confirmed visible params from real item pages

**Item 6254778 (MXEG080A45-01S05 — milling head):**
| Column | Title (full) | Value |
|--------|-------------|-------|
| `TAEG_DC` | Cutting diameter | 8.00 |
| `TAEG_RE` | Corner radius | 0.20 |
| `TAEG_PRFA` | Profile angle | 45.00 |
| `TAEG_LF` | Functional length | 10.00 |
| `TAEG_THSZMS` | connection thread nominal size machine side | S05 |

**Item 6127069 (solid end mill):**
| Column | Title (full) | Value |
|--------|-------------|-------|
| `TAEG_DC` | Cutting diameter | 8.00 |
| `TAEG_RE` | Corner radius | 4.00 |
| `TAEG_OAL` | Overall length | 100.00 |
| `TAEG_APMX` | Depth of cut maximum | 10.00 |
| `TAEG_LU` | Usable length | 30.0 |
| `TAEG_DN` | Neck diameter | 7.90 |
| `TAEG_DCONMS` | Connection diameter machine side | 8.00 |

### Other extractable fields (outside the parameters table)

| Data | HTML selector |
|------|---------------|
| Item designation | `<span id="content_lblItemDesignation">` |
| Family designation | `<a id="content_hlFamilyName">` |
| Family description | `<span id="content_lblFamilyDesc">` |
| Catalog No | First `<td class="ItemGridParamsValueWithLineBold">` in `content_gvwItemData` |
| Grade | `<a href="...Grade.aspx...">` inside `content_gvwItemData` |
| Items per package | `<span id="content_lblPackagePerItem">` |
| Family remarks | `<span id="content_lblFamilyRemarks">` |
| 2D image URL | `<input id="content_d2ImageReg" value="...">` |

---

## 4. Files to Create

### 4.1 `Models/TaeguTec/TaeguTecItemDto.cs`

```csharp
namespace AutoToolCatalog.Models.TaeguTec;

public class TaeguTecItemDto
{
    public string CatalogNo { get; set; } = "";
    public string ItemDesignation { get; set; } = "";
    public string FamilyDesignation { get; set; } = "";
    public string FamilyDescription { get; set; } = "";
    public string Grade { get; set; } = "";
    public string ItemsPerPackage { get; set; } = "";
    public string FamilyRemarks { get; set; } = "";
    public string? ImageUrl2D { get; set; }

    /// <summary>
    /// ISO13399 parameter codes → values. Keys are like "DC", "OAL", "APMX".
    /// These become TAEG_{key} columns in the output Excel.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new();
}
```

### 4.2 `Services/TaeguTec/TaeguTecApiClient.cs`

```csharp
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using AutoToolCatalog.Models.TaeguTec;

namespace AutoToolCatalog.Services.TaeguTec;

public class TaeguTecApiClient
{
    private const string BaseUrl = "https://www.imc-companies.com/taegutec/ttkcatalog/";
    private readonly HttpClient _httpClient;
    private readonly ILogger<TaeguTecApiClient> _logger;

    // Static shared session state — one warm session reused across lookups
    private static readonly SemaphoreSlim _warmupLock = new(1, 1);
    private static bool _sessionWarmed = false;

    public TaeguTecApiClient(HttpClient httpClient, ILogger<TaeguTecApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Fetch tool data for a given catalog number.
    /// Optionally pass a full item URL from the Excel Link column to skip search step.
    /// </summary>
    public async Task<TaeguTecItemDto?> FetchItemAsync(
        string catalogNo,
        string? knownItemUrl = null,
        CancellationToken ct = default)
    {
        await EnsureSessionWarmedAsync(ct);

        string? fnum = null;
        string? mapp = null;

        // Try to extract fnum/mapp from a known URL first (fastest path)
        if (!string.IsNullOrWhiteSpace(knownItemUrl))
        {
            var fnumFromUrl = Regex.Match(knownItemUrl, @"[?&]fnum=(\d+)");
            var mappFromUrl = Regex.Match(knownItemUrl, @"[?&]mapp=(\w+)");
            if (fnumFromUrl.Success) fnum = fnumFromUrl.Groups[1].Value;
            if (mappFromUrl.Success) mapp = mappFromUrl.Groups[1].Value;
        }

        // If we still don't have fnum, search for it
        if (fnum == null)
        {
            (fnum, mapp) = await ResolveFnumAsync(catalogNo, ct);
            if (fnum == null)
            {
                _logger.LogWarning("TaeguTec: could not resolve fnum for cat={CatalogNo}", catalogNo);
                return null;
            }
        }

        mapp ??= "ML";

        var itemUrl = $"{BaseUrl}Item.aspx?cat={catalogNo}&fnum={fnum}&mapp={mapp}&GFSTYP=M&srch=1";
        _logger.LogDebug("TaeguTec: fetching {Url}", itemUrl);

        string html;
        try
        {
            html = await _httpClient.GetStringAsync(itemUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TaeguTec: failed to fetch item page for cat={CatalogNo}", catalogNo);
            return null;
        }

        // Detect redirect to Search.aspx (session expired)
        if (html.Contains("id=\"content_gvwItemParameters\"") == false)
        {
            _logger.LogWarning("TaeguTec: item page for cat={CatalogNo} did not contain parameters table — possible session redirect", catalogNo);
            return null;
        }

        return ParseItemPage(html, catalogNo);
    }

    private async Task EnsureSessionWarmedAsync(CancellationToken ct)
    {
        if (_sessionWarmed) return;

        await _warmupLock.WaitAsync(ct);
        try
        {
            if (_sessionWarmed) return;
            _logger.LogInformation("TaeguTec: warming session via Index.aspx");
            await _httpClient.GetStringAsync(BaseUrl + "Index.aspx", ct);
            _sessionWarmed = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TaeguTec: session warmup failed (will retry on next call)");
        }
        finally
        {
            _warmupLock.Release();
        }
    }

    private async Task<(string? fnum, string? mapp)> ResolveFnumAsync(
        string catalogNo, CancellationToken ct)
    {
        var searchUrl = $"{BaseUrl}search.aspx?cat={catalogNo}&stype=1&styp=E";
        try
        {
            var searchHtml = await _httpClient.GetStringAsync(searchUrl, ct);
            var match = Regex.Match(searchHtml,
                $@"Item\.aspx\?cat={Regex.Escape(catalogNo)}&fnum=(\d+)&mapp=(\w+)",
                RegexOptions.IgnoreCase);
            if (match.Success)
                return (match.Groups[1].Value, match.Groups[2].Value);

            // Fallback: any fnum in the page for this cat
            var fallback = Regex.Match(searchHtml,
                $@"cat={Regex.Escape(catalogNo)}&fnum=(\d+)",
                RegexOptions.IgnoreCase);
            if (fallback.Success)
                return (fallback.Groups[1].Value, "ML");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TaeguTec: search failed for cat={CatalogNo}", catalogNo);
        }
        return (null, null);
    }

    private static TaeguTecItemDto ParseItemPage(string html, string catalogNo)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var dto = new TaeguTecItemDto
        {
            CatalogNo        = catalogNo,
            ItemDesignation  = GetText(doc, "content_lblItemDesignation"),
            FamilyDesignation= GetText(doc, "content_hlFamilyName"),
            FamilyDescription= GetText(doc, "content_lblFamilyDesc"),
            ItemsPerPackage  = GetText(doc, "content_lblPackagePerItem"),
            FamilyRemarks    = GetText(doc, "content_lblFamilyRemarks"),
            ImageUrl2D       = doc.GetElementbyId("content_d2ImageReg")
                                  ?.GetAttributeValue("value", null),
            Grade            = doc.GetElementbyId("content_gvwItemData")
                                  ?.SelectSingleNode(".//a[contains(@href,'Grade.aspx')]")
                                  ?.InnerText.Trim() ?? "",
        };

        // Parse the ISO13399 parameters table
        var paramTable = doc.GetElementbyId("content_gvwItemParameters");
        if (paramTable != null)
        {
            var rows = paramTable.SelectNodes(".//tr");
            if (rows?.Count >= 2)
            {
                // Collect visible header codes (th with class ItemGridParams1)
                var headerNodes = rows[0].SelectNodes(".//th") ?? new HtmlNodeCollection(null);
                var headers = headerNodes
                    .Select(th => new
                    {
                        Code    = th.InnerText.Trim().TrimEnd(),
                        IsVisible = th.GetAttributeValue("class", "")
                                     .Contains("ItemGridParams1")
                    })
                    .ToList();

                // Collect visible value cells (td with class ItemGridParamsValue1)
                var valueNodes = rows[1].SelectNodes(".//td") ?? new HtmlNodeCollection(null);
                var values = valueNodes
                    .Select(td => new
                    {
                        Value     = td.InnerText.Trim().TrimEnd('~').Trim(),
                        IsVisible = td.GetAttributeValue("class", "")
                                     .Contains("ItemGridParamsValue1")
                    })
                    .ToList();

                // Zip by index — header and value arrays are parallel
                for (int i = 0; i < Math.Min(headers.Count, values.Count); i++)
                {
                    if (!headers[i].IsVisible || !values[i].IsVisible) continue;
                    var code  = headers[i].Code.Trim();
                    var value = values[i].Value;
                    if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(value))
                        dto.Parameters[code] = value;
                }
            }
        }

        return dto;
    }

    private static string GetText(HtmlDocument doc, string id) =>
        doc.GetElementbyId(id)?.InnerText.Trim().TrimEnd() ?? "";
}
```

### 4.3 `Services/TaeguTec/TaeguTecProductDataProvider.cs`

```csharp
using AutoToolCatalog.Models;
using AutoToolCatalog.Models.TaeguTec;

namespace AutoToolCatalog.Services.TaeguTec;

public class TaeguTecProductDataProvider : IProductDataProvider
{
    private readonly TaeguTecApiClient _client;
    private readonly ILogger<TaeguTecProductDataProvider> _logger;

    public string SupplierName => SupplierPrefixes.TaeguTec;

    public TaeguTecProductDataProvider(
        TaeguTecApiClient client,
        ILogger<TaeguTecProductDataProvider> logger)
    {
        _client  = client;
        _logger  = logger;
    }

    public async Task<ProductFetchResult> FetchAsync(
        ToolRecord record,
        CancellationToken ct = default)
    {
        // Extract catalog number: prefer Link, else parse from Tool Description
        var catalogNo = ExtractCatalogNo(record.Link, record.ToolDescription);
        if (string.IsNullOrWhiteSpace(catalogNo))
        {
            return ProductFetchResult.Failure(
                "TaeguTec: cannot determine catalog number from Link or Tool Description");
        }

        TaeguTecItemDto? dto;
        try
        {
            dto = await _client.FetchItemAsync(catalogNo, record.Link, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TaeguTec: unhandled error for cat={CatalogNo}", catalogNo);
            return ProductFetchResult.Failure($"TaeguTec: {ex.Message}");
        }

        if (dto == null)
            return ProductFetchResult.Failure($"TaeguTec: no data returned for cat={catalogNo}");

        if (dto.Parameters.Count == 0)
            return ProductFetchResult.Failure($"TaeguTec: empty parameters for cat={catalogNo}");

        // Map Parameters dict → TAEG_{code} properties
        var properties = dto.Parameters
            .ToDictionary(
                kvp => $"{SupplierPrefixes.TaeguTecPrefix}{kvp.Key}",
                kvp => kvp.Value);

        // Add metadata columns
        if (!string.IsNullOrWhiteSpace(dto.ItemDesignation))
            properties[$"{SupplierPrefixes.TaeguTecPrefix}DESIGNATION"] = dto.ItemDesignation;
        if (!string.IsNullOrWhiteSpace(dto.Grade))
            properties[$"{SupplierPrefixes.TaeguTecPrefix}GRADE"] = dto.Grade;
        if (!string.IsNullOrWhiteSpace(dto.FamilyDesignation))
            properties[$"{SupplierPrefixes.TaeguTecPrefix}FAMILY"] = dto.FamilyDesignation.Trim();

        return ProductFetchResult.Success(properties);
    }

    /// <summary>
    /// Extract the 7-digit TaeguTec catalog number.
    /// From Link: ?cat=6254778 or /6254778.html or ends with a 7-digit number.
    /// From Tool Description: first 7-digit token.
    /// </summary>
    private static string? ExtractCatalogNo(string? link, string? description)
    {
        if (!string.IsNullOrWhiteSpace(link))
        {
            // ?cat=XXXXXXX
            var catParam = System.Text.RegularExpressions.Regex.Match(
                link, @"[?&]cat=(\d{6,8})");
            if (catParam.Success) return catParam.Groups[1].Value;

            // Bare number in URL path
            var urlNum = System.Text.RegularExpressions.Regex.Match(
                link, @"/(\d{6,8})(?:\.html?)?");
            if (urlNum.Success) return urlNum.Groups[1].Value;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            var descNum = System.Text.RegularExpressions.Regex.Match(
                description, @"\b(\d{6,8})\b");
            if (descNum.Success) return descNum.Groups[1].Value;
        }

        return null;
    }
}
```

---

## 5. Files to Modify

### 5.1 `Models/SupplierPrefixes.cs`

Add the TaeguTec entries alongside the existing constants:

```csharp
// ADD these lines:
public const string TaeguTec       = "TAEGUTEC";
public const string TaeguTecPrefix = "TAEG_";

// Also update the normalization dictionary/switch that maps
// raw Excel supplier cell values to canonical names.
// Add entries for: "TAEGUTEC", "TAEGU", "TAEG", "IMC"
// so that any of these in the Procurement channel column resolves to TaeguTec.
```

Find the existing normalization logic (likely a `switch` or `Dictionary<string, string>` in `SupplierPrefixes.cs` or `ExcelService.cs`) and add:
```csharp
"TAEGUTEC" => SupplierPrefixes.TaeguTec,
"TAEGU"    => SupplierPrefixes.TaeguTec,
"TAEG"     => SupplierPrefixes.TaeguTec,
"IMC"      => SupplierPrefixes.TaeguTec,
```

### 5.2 `Program.cs`

**a) Register the named HttpClient** (add alongside existing KENNAMETAL, SANDVIK, WALTER):

```csharp
builder.Services.AddHttpClient<TaeguTecApiClient>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept",
        "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    CookieContainer     = new System.Net.CookieContainer(),
    UseCookies          = true,
    AllowAutoRedirect   = true,
    // Important: share the cookie container so session cookie persists
    // across the lifetime of this named client
});
```

**b) Register the provider** (add alongside existing Sandvik/Walter providers):

```csharp
builder.Services.AddScoped<TaeguTecProductDataProvider>();
```

**c) Register in `ProductDataProviderRegistry`** (find where WALTER is registered and add TaeguTec after it):

```csharp
// In ProductDataProviderRegistry constructor or wherever providers are added:
_providers[SupplierPrefixes.TaeguTec] = serviceProvider
    .GetRequiredService<TaeguTecProductDataProvider>();
```

> **Note:** Check how the existing registry is wired. If it uses a `Dictionary` populated in the constructor, add the TaeguTec entry there. If it uses a list and resolves by `SupplierName` property, just registering the DI service is enough.

### 5.3 `AutoToolCatalog.csproj`

Add the NuGet package (already used elsewhere in the project but confirm it's referenced):

```xml
<PackageReference Include="HtmlAgilityPack" Version="1.11.*" />
```

If it is already present (check existing `<PackageReference>` entries), no change needed.

### 5.4 `README.md`

Update the **Dynamic columns** table to show TaeguTec status as "Live":

```markdown
| TAEGUTEC   | `TAEG_`  | HTML catalog scrape — session-aware HttpClient (no Playwright) |
```

Update the **Supplier normalization** section (if present) to show TaeguTec aliases.

Update CHANGELOG.md:
```markdown
## [X.X.X] - {date}
### Added
- TaeguTec (IMC e-catalog) supplier pipeline via session-aware HTML scraper
- TAEG_ column prefix; columns generated dynamically from ISO13399 parameter table
- Catalog number resolved from Link (?cat=XXXXX), URL path, or Tool Description
```

---

## 6. NuGet Dependency

| Package | Version | Purpose |
|---------|---------|---------|
| `HtmlAgilityPack` | `1.11.*` | Parse `content_gvwItemParameters` table |

Run `dotnet add package HtmlAgilityPack` if not already present. Verify with:
```
grep -r "HtmlAgilityPack" AutoToolCatalog.csproj
```

---

## 7. Rate Limiting & Politeness

The TaeguTec site has no documented rate limit but is a shared IIS host. Add a delay in `TaeguTecProductDataProvider.FetchAsync` between the search call and item call — this is handled inside `TaeguTecApiClient` naturally since search + item = 2 sequential HTTP calls per row.

`ScraperService` already enforces max 5 concurrent rows globally. The TaeguTec session warmup is serialized via `SemaphoreSlim(1,1)` inside `TaeguTecApiClient` so only one thread warms the session.

Do **not** add `Thread.Sleep` or `Task.Delay` — the sequential HTTP calls already provide natural pacing.

---

## 8. Excel Input Conventions

| Column | Expected value |
|--------|---------------|
| Procurement channel / Supplier | `TAEGUTEC`, `TAEGU`, `TAEG`, or `IMC` |
| Link | Full URL: `https://www.imc-companies.com/.../Item.aspx?cat=6254778&fnum=11154&mapp=ML...` — OR bare: `item.aspx?cat=6254778` — OR omit entirely if catalog no. is in Tool Description |
| Tool Description | May contain 7-digit catalog number as a token (e.g., `6254778` or `HSF 6254778 250`) |

When Link contains a full item URL with `fnum`, the fnum-resolution search step is skipped entirely (fastest path, ~1 HTTP request).

---

## 9. Output Column Examples

After processing, the export Excel will contain dynamic columns like:

| TAEG_DC | TAEG_RE | TAEG_OAL | TAEG_APMX | TAEG_LU | TAEG_GRADE | TAEG_DESIGNATION | TAEG_FAMILY |
|---------|---------|----------|-----------|---------|-----------|-----------------|-------------|
| 8.00 | 4.00 | 100.00 | 10.00 | 30.0 | TT5505 | BNE4100-02-S05 | MXEG-01 |

Columns appear only for parameters present in the fetched item — different tool families expose different ISO13399 params (a milling head shows `PRFA`; a solid end mill shows `OAL`, `APMX`, `LU`, `DN`).

---

## 10. Test Cases

Use these known catalog numbers to verify the implementation:

| Catalog No | Expected designation | Expected family | Key params |
|-----------|---------------------|-----------------|-----------|
| `6254778` | `MXEG080A45-01S05` | `MXEG-01` | DC=8.00, RE=0.20, PRFA=45.00, LF=10.00 |
| `6127069` | `HSB 2080 100 300` | `HSB 2 (6.0-12.0)` | DC=8.00, RE=4.00, OAL=100.00, APMX=10.00 |

Manual test command (add a test row to sample Excel):
```
No. | Tool Description | Procurement channel | Link
1   | 6254778          | TAEGUTEC            | https://www.imc-companies.com/taegutec/ttkcatalog/Item.aspx?cat=6254778&fnum=11154&mapp=ML&GFSTYP=M&srch=1
2   | 6127069          | TAEGUTEC            | https://www.imc-companies.com/TaeguTec/ttkCatalog/item.aspx?cat=6127069&fnum=10091&mapp=ML&app=71&GFSTYP=M&isoD=1
```

Row 1 should use the fast path (fnum from Link). Row 2 should resolve fnum via search.

---

## 11. File Structure After Implementation

```
Auto-Tool-Catalog/
├── Models/
│   └── TaeguTec/
│       └── TaeguTecItemDto.cs          ← NEW
├── Services/
│   └── TaeguTec/
│       ├── TaeguTecApiClient.cs        ← NEW
│       └── TaeguTecProductDataProvider.cs  ← NEW
├── Models/
│   └── SupplierPrefixes.cs             ← MODIFIED (add TaeguTec + aliases)
└── Program.cs                          ← MODIFIED (DI + HttpClient registration)
```

---

## 12. Implementation Checklist

- [ ] Create `Models/TaeguTec/TaeguTecItemDto.cs`
- [ ] Create `Services/TaeguTec/TaeguTecApiClient.cs`
- [ ] Create `Services/TaeguTec/TaeguTecProductDataProvider.cs`
- [ ] Add `TaeguTec` and `TaeguTecPrefix` constants to `SupplierPrefixes.cs`
- [ ] Add normalization aliases (`TAEGUTEC`, `TAEGU`, `TAEG`, `IMC`) to supplier normalization logic
- [ ] Register `AddHttpClient<TaeguTecApiClient>` in `Program.cs` with `CookieContainer`
- [ ] Register `TaeguTecProductDataProvider` as scoped in `Program.cs`
- [ ] Register in `ProductDataProviderRegistry`
- [ ] Confirm `HtmlAgilityPack` NuGet is in `.csproj`
- [ ] Test with `cat=6254778` (with Link → fast path)
- [ ] Test with `cat=6127069` (no Link → search path)
- [ ] Verify `TAEG_DC`, `TAEG_GRADE`, `TAEG_DESIGNATION` appear in export

---

## 13. Gotchas & Edge Cases

1. **Session expiry:** The static `_sessionWarmed` flag is never reset. If the app runs for a long time and the session cookie expires, item fetches will silently return the search listing page. Detect this by checking if `content_gvwItemParameters` is absent in the response and re-warm if needed. The current code already does this check and returns `null`, which results in a row failure — acceptable for now.

2. **`fnum` not found in search:** Some catalog numbers may not appear in the search results (discontinued, region-restricted). The provider returns a `Failure` result with a descriptive message.

3. **Trailing spaces in cell values:** The TaeguTec HTML pads all strings to fixed width. `InnerText.Trim().TrimEnd()` handles this but double-check with `TrimEnd()` after the `TrimEnd('~')` as well.

4. **Mixed visible/hidden column ordering:** The `<th>` and `<td>` arrays are always parallel (same index = same column). Do NOT filter the hidden ones out before zipping — use the index-based approach shown in the parser. Only skip output for hidden indices.

5. **`HtmlNodeCollection` null:** `SelectNodes` returns `null` (not empty) when there are no matches. Always null-check before iterating.

6. **MonsterASP shared hosting:** The `CookieContainer` on the `HttpClientHandler` must be configured at registration time (not in the `client =>` lambda). The handler instance must be long-lived (i.e., not created per-request) — `AddHttpClient` with `ConfigurePrimaryHttpMessageHandler` ensures the handler is reused.
