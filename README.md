<<<<<<< HEAD
# Auto Tool Catalog Enricher

A web application that automatically enriches a tooling Excel database with technical specifications by scraping official supplier websites.

## Features

- Import Excel (`.xlsx`) with ~3000 tooling records
- Display data in a table preview
- Fetch missing tool specs from supplier websites (SECO, Kennametal, Sandvik, Walter)
- Real-time progress bar (percent, success/fail counts, current item)
- Export completed Excel file
- 100% free stack (no paid APIs)

## Tech Stack

- **Backend:** ASP.NET Core (.NET 8)
- **Frontend:** Razor Pages, Bootstrap
- **Excel:** ClosedXML
- **Scraping:** HttpClient + HtmlAgilityPack
- **Progress:** SignalR + polling fallback

## Architecture

```
/
├── Hubs/
│   └── ProcessingHub.cs          # SignalR hub for real-time progress
├── Models/
│   ├── ToolRecord.cs             # Excel row model
│   ├── ToolSpecResult.cs         # Scraped spec result
│   ├── ProcessingProgress.cs     # Progress DTO
│   └── ProcessSession.cs         # In-memory session
├── Services/
│   ├── IExcelService.cs / ExcelService.cs
│   ├── IScraperService.cs / ScraperService.cs
│   ├── ISupplierParser.cs
│   ├── SupplierParsers/
│   │   ├── BaseSupplierParser.cs
│   │   ├── SecoParser.cs
│   │   ├── KennametalParser.cs
│   │   ├── SandvikParser.cs
│   │   └── WalterParser.cs
│   └── ProcessSessionStore.cs
└── Pages/
    └── Index.cshtml              # Main UI
```

## Input Excel Format

| Col | Name                                |
|-----|-------------------------------------|
| 1   | No.                                 |
| 2   | Tool Description                    |
| 3   | Type of Tool                        |
| 4   | Shank Ø (DMM) / Bore Ø (DCB)        |
| 5   | Tool Ø (DC)                         |
| 6   | Corner rad                          |
| 7   | Flute / Cutting edge length (APMXS) |
| 8   | Overall length (OAL / LF)           |
| 9   | Peripheral cutting edge count       |
| 10  | Procurement channel                 |

Columns 2, 3, and 10 are used to determine search context. Columns 4–9 are filled from scraped specs (or `#NA` if not found).

## Supported Suppliers

- SECO – https://www.secotools.com
- KENNAMETAL – https://www.kennametal.com
- SANDVIK – https://www.sandvik.coromant.com
- WALTER – https://www.walter-tools.com

## Deployment

### Prerequisites

- .NET 8 SDK
- Windows, Linux, or macOS

### Run Locally

```bash
cd Auto-Tool-Catalog
dotnet restore
dotnet run
```

Open https://localhost:5000 (or the URL shown in the console).

### Publish for Production

```bash
dotnet publish -c Release -o ./publish
cd publish
dotnet AutoToolCatalog.dll
```

### Docker (optional)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY ./publish .
ENTRYPOINT ["dotnet", "AutoToolCatalog.dll"]
```

```bash
dotnet publish -c Release -o ./publish
docker build -t auto-tool-catalog .
docker run -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 auto-tool-catalog
```

### IIS (Windows)

1. Publish: `dotnet publish -c Release -o C:\inetpub\AutoToolCatalog`
2. Create Application Pool (No Managed Code)
3. Create Application pointing to the publish folder
4. Ensure ASP.NET Core Hosting Bundle is installed

## Configuration

- Max concurrent scrapers: 5
- Retry count: 3
- Timeout: 15 seconds per request

## Error Handling

| Case            | Action            |
|-----------------|-------------------|
| Website down    | Mark row as failed|
| Tool not found  | Fill `#NA`        |
| Spec missing    | Fill `#NA`        |
| Timeout         | Retry 3 times     |

## Optional: Local LLM Fallback

For complex or changing HTML, you can add a local LLM (e.g. Ollama) to interpret messy tables:

```csharp
string prompt = $"""
Extract DC, APMX, RE, ZEFP, OAL, DCONMS from this HTML. Return JSON.
HTML:
{html}
""";
// Call Ollama API, parse JSON response, map to ToolSpecResult
```

## Adding New Suppliers

1. Create a new parser in `Services/SupplierParsers/` inheriting `BaseSupplierParser`
2. Implement `FetchSpecsCoreAsync` with search and spec extraction logic
3. Register in `Program.cs`:

```csharp
builder.Services.AddHttpClient("NEWSUPPLIER", c => { ... });
builder.Services.AddScoped<ISupplierParser, NewSupplierParser>();
```

## License

MIT
=======
# Auto-Tool-Catalog
Auto Tool Spec Retrieval from Supplier Website
>>>>>>> 8f82939c3af6a828f94d06e359f43c47743f92bc
