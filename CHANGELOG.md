# Changelog

All notable changes to Auto Tool Catalog are documented in this file.

## [2.12.4] - 2026-06-03

### Changed
- Documented the `BROWSERBASE_API_KEY` deployment step: added it to the README MonsterASP control-panel section (with a note that `appsettings.Development.json` is not used in Production) and to the `publish-for-ftp.ps1` output reminder.

## [2.12.3] - 2026-06-03

### Changed
- Updated `README.md` to reflect the current state: TaeguTec via Browserbase, version 2.12.x, features/tech-stack/architecture/project-layout entries, and the changelog history footer.

## [2.12.2] - 2026-06-03

### Changed
- Replaced the SECO processing note on the main page with a TaeguTec note explaining the slower Browserbase fetch (~20–40 s per item, ~2 sessions at a time) so the longer run times are expected.

## [2.12.1] - 2026-06-03

### Fixed
- **Browserbase concurrent-session 429s** — the parallel scraper (5 rows at once) opened more cloud-browser sessions than the Browserbase plan allows (free = 3). `TaeguTecBrowserbaseFetcher` now gates session creation with a semaphore (`TaeguTec:BrowserbaseMaxConcurrency`, default 2) and retries `429 Too Many Requests` with exponential backoff (honoring `Retry-After`), so TaeguTec rows queue instead of failing.

## [2.12.0] - 2026-06-03

### Added
- **TaeguTec via Browserbase cloud browser** — the IMC e-catalog is Cloudflare-protected (403 JS challenge), which plain HTTP cannot pass. `TaeguTecBrowserbaseFetcher` drives a Browserbase cloud browser over raw Chrome DevTools Protocol (WebSocket) to warm the session, resolve `fnum`/`mapp`, and load `Item.aspx`. The browser runs in Browserbase's cloud, so **no Chromium/Playwright driver is needed on the server** (MonsterASP-safe).
- `ITaeguTecItemFetcher` abstraction with two implementations: `TaeguTecHttpSession` (plain HTTP) and `TaeguTecBrowserbaseFetcher`. Selected at startup based on `TaeguTec:BrowserbaseApiKey` (or `BROWSERBASE_API_KEY` env var); falls back to HTTP when unset.
- Shared `TaeguTecHtmlParser` used by both fetchers.

### Configuration
- `appsettings.json` → `TaeguTec:BrowserbaseApiKey` / `BrowserbaseProjectId` (or env `BROWSERBASE_API_KEY` / `BROWSERBASE_PROJECT_ID`). Startup logs the active fetch mode.

## [2.11.0] - 2026-06-03

### Added
- **TaeguTec master catalog** (`Data/TAEGUTEC_CATALOG_NO.xlsx`) seeded into SQLite (`taegutec_catalog`) via `TaeguTecCatalogStore` — mirrors `SecoGlobalIdStore` but maps **Tool Description → Catalog No**. Resolves designation-only rows (e.g. `MXEG080A45-01S05`) before fetching `Item.aspx` specs.

### Changed
- `TaeguTecApiClient` resolves catalog number from Link, then master list, then description fallback.

## [2.10.0] - 2026-06-03

### Added
- **TaeguTec HTTP catalog integration** — `TaeguTecHttpSession` (ASP.NET session warmup + cookie jar), `TaeguTecApiClient` (HtmlAgilityPack parse of `Item.aspx`), and `TaeguTecProductDataProvider`. Resolves catalog number from Link or Tool Description, discovers `fnum`/`mapp` via `search.aspx` when needed, and maps visible ISO13399 parameters to `TAEG_*` columns.

### Changed
- **TAEGUTEC** is API-supported again (`SupplierPrefixes.IsApiSupported`); registry uses live provider instead of stub.

## [2.9.0] - 2026-06-03

### Removed
- All TaeguTec live integration (`TaeguTecApiClient`, `TaeguTecBrowserFetcher`, `TaeguTecHtmlParser`, `TaeguTecCatalogStore`, `TAEGUTEC_CATALOG.xlsx`, TaeguTest/TaeguProbe tools). The IMC e-catalog is Cloudflare-blocked from server/datacenter IPs and cannot be fetched reliably.

### Changed
- **TAEGUTEC** rows are still recognized on Excel import but use `StubProductDataProvider` — processing completes successfully with dynamic spec columns set to **`#N/A`** (no network calls, no Playwright).

## [2.8.0] - 2026-06-03

### Added
- **TaeguTec offline catalog** (`Data/TAEGUTEC_CATALOG.xlsx`) seeded into SQLite (`taegutec_catalog`) and an in-memory dictionary at startup via new `TaeguTecCatalogStore` — mirrors `SecoGlobalIdStore` but keyed on **Catalog No**. Resolves `Tool Description → Catalog No` with no network call.
- The master Excel may carry extra spec columns (DC, OAL, APMX, Grade, …) beyond `Catalog No` + `Tool Description`; when present, the row is served **fully offline** as `TAEG_*` properties — required because the IMC e-catalog is Cloudflare-blocked (403) from datacenter IPs and the browser is disabled on MonsterASP.

### Changed
- `TaeguTecApiClient` now checks the offline catalog first, then HTTP/browser. When the page is unreachable but a Catalog No is known, it still returns `TAEG_CATALOG_NO` + product URL instead of failing the row.
- `TaeguTecBrowserFetcher` no longer attempts a runtime Chromium install when `DISABLE_PLAYWRIGHT_INSTALL=true` (prevents the MonsterASP app-pool suspension); falls back to the offline catalog.

## [2.7.2] - 2026-06-03

### Fixed
- SECO designation search hardened: homepage cookie warmup, `market`/`language` on `SearchProducedProducts`, designation dash/whitespace normalization, 60 s HTTP timeout, clearer error messages.
- `appsettings.Production.json` now includes `Seco:Market` and `Seco:Language` defaults.

## [2.7.1] - 2026-06-03

### Fixed
- SECO designation-only rows now resolve via `Products/SearchProducedProducts` (GET with `searchTerms`) instead of broken 404 search endpoints and non-rendered `/search?q=` HTML. Exact designation match is preferred when multiple variants are returned.

## [2.7.0] - 2026-06-03

### Changed
- SECO pipeline no longer uses Playwright/Chromium — product data is fetched via pure `HttpClient` + `CookieContainer` against `GetFullProduct` (POST with `itemNumber`, `market`, `language` after article-page warmup). Eliminates Node.exe/Chromium on the server for SECO rows and avoids MonsterASP suspension.
- Removed `SecoPlaywrightPool` and `SecoBrowserApiFetcher`; `SecoHttpSession` is now a DI singleton with session cookie warmup and API headers matching browser DevTools.
- `PlaywrightBootstrap` and startup respect `DISABLE_PLAYWRIGHT_INSTALL=true`; `scripts/publish-for-ftp.ps1` strips `.playwright` from publish output.
- Configurable `Seco:Market` (default `MY`) and `Seco:Language` (default `en-GB`) in `appsettings.json`.

### Note
- Playwright remains in the project for Kennametal and TaeguTec browser fallbacks only; set `DISABLE_PLAYWRIGHT_INSTALL=true` on MonsterASP if those suppliers are not used in production.

## [2.6.0] - 2026-06-02

### Added
- TaeguTec (IMC e-catalog) integration: no public JSON API — specifications (DC, OAL, APMX, etc.) are read from the **item.aspx HTML table** via `TaeguTecHtmlParser`, with Playwright fallback when Cloudflare blocks plain HTTP.
- Resolve catalog number from Link (`cat=6127491`) or search by tool description (e.g. `HSF 6050XLT 250`). Dynamic columns use `TAEG_` prefix.

## [2.5.3] - 2026-06-02

### Fixed
- MonsterASP hosting: publish **DLL only** (`UseAppHost=false`), force **InProcess** hosting model, skip Playwright install on production startup, exclude `.playwright` from publish; README documents app-pool disabled error and control-panel settings.

## [2.5.2] - 2026-06-02

### Changed
- `scripts/publish-for-ftp.ps1` now publishes to `C:\Users\Public\Documents\Auto-Tool-Catalog\publish_clean` by default (optional `-OutputPath` override).

## [2.5.1] - 2026-06-02

### Changed
- GitHub Actions deploy workflow switched from Web Deploy to **FTP**: every run uploads a **monsterasp-publish** artifact for manual copy to `wwwroot`; optional FTP upload when you run the workflow manually with **Deploy to FTP** enabled (secrets: `FTP_SERVER`, `FTP_USERNAME`, `FTP_PASSWORD`).
- Added `scripts/publish-for-ftp.ps1` and README instructions for MonsterASP FTP / WebFTP manual deployment.

## [2.5.0] - 2026-06-02

### Added
- GitHub Actions workflow (`.github/workflows/deploy.yml`): builds and publishes the web project, then deploys to MonsterASP.NET via Web Deploy (port 8172) on every push to `main`/`master` (and manual `workflow_dispatch`). Credentials are read from repository secrets (`WEBSITE_NAME`, `SERVER_COMPUTER_NAME`, `SERVER_USERNAME`, `SERVER_PASSWORD`).
- `Data/SECO_GLOBAL_ID.xlsx` is now copied to the publish output (`CopyToPublishDirectory`) so the SECO master list seeds on the server.

## [2.4.0] - 2026-06-02

### Added
- SECO master list: `Data/SECO_GLOBAL_ID.xlsx` (~1,000 tools) is seeded into a SQLite table (`seco_global_ids`) and loaded into an in-memory dictionary at startup for O(1) `Tool Description → Seco Global Number` lookups.
- New `SecoGlobalIdStore` service; re-seeds automatically only when the Excel file changes (tracked via `app_meta` signature).

### Changed
- SECO item-number resolution now checks the Link, then the master list, before any network/browser work. Designation-only rows that match the master list skip site search and go straight to the fast in-page fetch (~6–7 s warm vs ~30–90 s previously).

## [2.3.3] - 2026-06-02

### Changed
- SECO performance: reuse one Playwright Chromium instance per app run; fetch `GetFullProduct` via in-page `fetch` when item number is known (avoids launching a new browser per row).
- SECO HTTP item resolution uses a shared warmed cookie session (single homepage warmup per process).
- SECO designation search tries `/search?q=` navigation before the slower search-box UI flow.

## [2.3.2] - 2026-06-02

### Fixed
- Kennametal part numbers (e.g. `5720VZ16-A063Z4R`) now resolve via Hybris product search API (`/ws/v2/kmt/products/search`) to numeric CAD IDs such as `5672870`.

## [2.3.1] - 2026-06-02

### Changed
- README updated for v2.3.x: Walter API pipeline, all four live suppliers, project layout, error handling, and changelog summary.

## [2.2.0] - 2026-06-02

### Added
- Sandvik product data via `https://www.sandvik.coromant.com/api/productsearch/product?id={materialId}`.
- Resolves material ID from Sandvik product URLs (`m=` query) or autocomplete search by tool description.
- Dynamic columns prefixed `SAND_` (e.g. `SAND_DC`, `SAND_APMX`, `SAND_OAL`) from product detail properties.

## [2.3.0] - 2026-06-02

### Added
- Walter product data via `https://www.walter-tools.com/api/productsearch/getproduct?id={id}&measurementUnit=Metric&language=en-gb`.
- Dynamic columns prefixed `WALT_` from Walter product `columns` + first `items[]` record.

## [2.2.1] - 2026-06-02

### Added
- Processing elapsed time display (`Time: hh:mm:ss`) in the progress panel.

## [2.1.0] - 2026-06-02

### Added
- Kennametal product data via `https://www.product-config.net/catalog3/cad?d=kennametal&id={productId}`.
- Resolves product ID from Kennametal product URLs (`.{id}.html`) or site search by tool description.
- Dynamic columns prefixed `KENN_` (e.g. `KENN_D_1`, `KENN_L_BIT_OAL`, `KENN_Z`) from CAD `attributes` + `attributeValues`.

## [2.0.5] - 2026-06-02

### Changed
- README rewritten for v2.0 architecture (API pipeline, SQLite, dynamic columns, SECO Playwright bridge, project layout, API reference).

## [2.0.4] - 2026-06-02

### Changed
- Moved the SECO browser-lookup timing note out of the toolbar (it broke the header layout) into the progress panel, below the Success/Failed row.
- Exported Excel is now named after the source file with an `_updated` suffix (e.g. `Tool Database_Test_Seco.xlsx` → `Tool Database_Test_Seco_updated.xlsx`).

## [2.0.3] - 2026-06-02

### Fixed
- SECO rows without a Link column (e.g. `Tool Database_Test_Seco.xlsx`) now resolve products via Playwright site search + product page API capture.
- Browser fallback runs when HTTP cannot fetch product JSON; captures `GetFullProduct` response from the product page (HTTP 405 bypass).
- Fixed Playwright session setup so SECO search input is found (cookie dismiss + default browser context).
- SECO attribute values deserialize correctly when the API returns numbers instead of strings.
- Excel import maps alternate Link column headers (Webpage Link, URL, Product URL).

## [2.0.2] - 2026-06-02

### Fixed
- SECO `GetFullProduct` now falls back to Playwright in-page `fetch` when direct HTTP returns 405, so product attributes populate after Process.
- Playwright Chromium is installed on startup for the SECO API bridge.

## [2.0.1] - 2026-06-02

### Fixed
- Supplier routing bug where KENNAMETAL/SANDVIK/WALTER incorrectly matched the SECO provider (`Contains("SECO")`).
- SECO API client now uses cookie warmup and improved item-number search before calling GetFullProduct.
- Excel import reads Supplier from Procurement channel when the Supplier cell is not a known vendor.
- SignalR reconnect re-joins session; table refreshes when processing completes.

## [2.0.0] - 2026-06-02

### Changed
- **BREAKING:** Replaced HTML scraping (HttpClient/HtmlAgilityPack/Playwright) with supplier API pipeline.
- SECO products fetched via `GetFullProduct` API; raw JSON and normalized attributes stored in SQLite.
- Data Preview uses dynamic property columns (`SECO_*`, `KENN_*`, etc.) instead of fixed spec columns.
- Excel import reads only core columns: No., Tool Description, Supplier, Link.
- Excel export includes core columns plus all dynamic property columns.

### Removed
- Supplier HTML parsers, tool-type mapping logic, and fixed spec columns (Type, Shank/Bore Ø, Tool Ø, etc.).
- HtmlAgilityPack and Microsoft.Playwright dependencies.

### Added
- SQLite catalog database for raw product JSON and normalized attributes.
- Stub providers for Kennametal, Sandvik, and Walter (return `#N/A` until APIs are implemented).

## [1.2.7] - 2026-06-02
