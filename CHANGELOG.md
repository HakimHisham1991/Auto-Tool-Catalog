# Changelog

All notable changes to Auto Tool Catalog are documented in this file.

## [1.2.7] - 2026-06-02

### Changed
- Excel export columns match the web Data Preview layout and headers.
- Export auto-fits column widths, center/middle-aligns all cells, and applies Table Style Medium 6.
- Excel import column order updated to match export.

## [1.2.6] - 2026-06-02

### Changed
- Link column icon changed from pin to paper clip.
- Data Preview table shows borders on all cells.

## [1.2.5] - 2026-06-02

### Changed
- Renamed Webpage Link column to **Link** with auto-fit width.
- Link cells show a blue pin icon (hover for full URL, click opens new tab); Excel export still contains full hyperlinks.

## [1.2.4] - 2026-06-02

### Changed
- Webpage Link header uses the same font size as other column headers; only cell contents are half-size.
- Webpage Link column: no wrap, ellipsis cutoff at header text width.

## [1.2.3] - 2026-06-02

### Changed
- Moved Supplier and Webpage Link columns to immediately after Tool Description.
- Webpage Link column: fixed 10-character width, text wrap, and half-size font.

## [1.2.2] - 2026-06-02

### Fixed
- Background scraping now runs in its own DI scope so parsers are not disposed mid-run.
- Playwright Chromium is auto-installed on app startup when missing.
- Scraper now overwrites existing `#NA` cells and counts success only when specs or a webpage link are found.

## [1.2.1] - 2026-06-02

### Added
- Progress bar during Excel import showing upload, server parsing, and preview loading status.

## [1.2.0] - 2026-06-02

### Added
- **Webpage Link** column after Supplier in the data preview table, export, and scraper output.
- Product page URLs are captured during scraping and shown as clickable links in the UI and Excel export.

## [1.1.5] - 2026-06-02

### Changed
- Reduced header title font size to match the version line.

## [1.1.4] - 2026-06-02

### Changed
- Moved app title, version, attribution, and tagline into the page header.
- Removed navbar brand and Catalog navigation links.
- Updated tagline to reference full tooling data export.

## [1.1.3] - 2026-06-02

### Fixed
- Pinned `System.IO.Packaging` to 9.0.0 to resolve NU1903 vulnerability warnings from ClosedXML's transitive dependency.

## [1.1.2] - 2026-06-02

### Changed
- Upgraded the application from .NET 8 to .NET 10.
- Renamed the product from "Tool Catalog Enricher" to "Auto Tool Catalog" across the UI and documentation.

### Added
- Version number and "Developed by UPECA PDC" attribution on the main page.
