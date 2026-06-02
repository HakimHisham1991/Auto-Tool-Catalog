# Changelog

All notable changes to Auto Tool Catalog are documented in this file.

## [2.2.0] - 2026-06-02

### Added
- Sandvik product data via `https://www.sandvik.coromant.com/api/productsearch/product?id={materialId}`.
- Resolves material ID from Sandvik product URLs (`m=` query) or autocomplete search by tool description.
- Dynamic columns prefixed `SAND_` (e.g. `SAND_DC`, `SAND_APMX`, `SAND_OAL`) from product detail properties.

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
