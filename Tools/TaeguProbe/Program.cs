using AutoToolCatalog.Models;
using AutoToolCatalog.Services.TaeguTec;
using Microsoft.Extensions.Logging;

var apiKey = Environment.GetEnvironmentVariable("BROWSERBASE_API_KEY")
    ?? (args.Length > 0 ? args[0] : null);
var projectId = Environment.GetEnvironmentVariable("BROWSERBASE_PROJECT_ID")
    ?? (args.Length > 1 ? args[1] : null);

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Usage: set BROWSERBASE_API_KEY (and optionally BROWSERBASE_PROJECT_ID) or pass as args.");
    return;
}

var dataDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Data"));
var store = new TaeguTecCatalogStore(
    Path.Combine(dataDir, "TAEGUTEC_CATALOG_NO.xlsx"),
    Path.Combine(dataDir, "catalog.db"));
store.Initialize();
Console.WriteLine($"Catalog entries: {store.Count}");

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Debug));
var logger = loggerFactory.CreateLogger<TaeguTecBrowserbaseFetcher>();
var fetcher = new TaeguTecBrowserbaseFetcher(apiKey, projectId, logger);
var client = new TaeguTecApiClient(fetcher, store);

string[] descs = ["MXEG080A45-01S05", "HSB 2080 100 300"];
foreach (var d in descs)
{
    store.TryResolve(d, out var cat);
    Console.WriteLine($"--- {d} (cat={cat}) ---");
    var result = await client.FetchProductAsync(new ToolRecord
    {
        ToolDescription = d,
        ProcurementChannel = "TAEGUTEC"
    });
    Console.WriteLine($"ok={result.Success} props={result.Properties.Count} err={result.ErrorMessage}");
    foreach (var kv in result.Properties.Take(12))
        Console.WriteLine($"   {kv.Key} = {kv.Value}");
}
