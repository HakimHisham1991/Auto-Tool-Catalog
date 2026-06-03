using AutoToolCatalog.Models;
using AutoToolCatalog.Services.TaeguTec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var dataDir = Path.Combine(projectRoot, "Data");

var config = new ConfigurationBuilder()
    .SetBasePath(projectRoot)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = config["TaeguTec:BrowserbaseApiKey"] ?? Environment.GetEnvironmentVariable("BROWSERBASE_API_KEY");
var projectId = config["TaeguTec:BrowserbaseProjectId"] ?? Environment.GetEnvironmentVariable("BROWSERBASE_PROJECT_ID");
var maxConcurrency = config.GetValue<int?>("TaeguTec:BrowserbaseMaxConcurrency") ?? 2;

Console.WriteLine($"Project root: {projectRoot}");
Console.WriteLine($"Data dir: {dataDir}");
Console.WriteLine($"Browserbase key set: {!string.IsNullOrWhiteSpace(apiKey)} (len={apiKey?.Length ?? 0})");
Console.WriteLine($"Max concurrency: {maxConcurrency}");

var excelPath = Path.Combine(dataDir, "TAEGUTEC_CATALOG_NO.xlsx");
Console.WriteLine($"Excel exists: {File.Exists(excelPath)}");

var store = new TaeguTecCatalogStore(excelPath, Path.Combine(dataDir, "catalog.db"));
store.Initialize();
Console.WriteLine($"Catalog entries: {store.Count}");

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Debug));
ITaeguTecItemFetcher fetcher;
if (!string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Mode: Browserbase");
    fetcher = new TaeguTecBrowserbaseFetcher(apiKey, projectId, loggerFactory.CreateLogger<TaeguTecBrowserbaseFetcher>(), maxConcurrency);
}
else
{
    Console.WriteLine("Mode: HTTP (no Browserbase key)");
    fetcher = new TaeguTecHttpSession();
}

var client = new TaeguTecApiClient(fetcher, store, new TaeguTecRuntimeInfo
{
    UsesBrowserbase = !string.IsNullOrWhiteSpace(apiKey),
    CatalogExcelPath = excelPath
});

string[] descs = ["MXEG080A45-01S05", "MXCSO 4120R075-S08", "HSB 2080 100 300", "HES 6120T", "TEO S041-6"];
foreach (var d in descs)
{
    store.TryResolve(d, out var cat);
    var result = await client.FetchProductAsync(new ToolRecord
    {
        ToolDescription = d,
        ProcurementChannel = "TAEGUTEC"
    });
    Console.WriteLine($"[{d}] cat={cat ?? "MISS"} ok={result.Success} props={result.Properties.Count} err={result.ErrorMessage}");
}
