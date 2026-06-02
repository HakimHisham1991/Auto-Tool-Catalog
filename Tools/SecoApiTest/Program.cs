using System.Diagnostics;
using AutoToolCatalog.Models;
using AutoToolCatalog.Services.Seco;

var dataDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Data");
var excelPath = Path.GetFullPath(Path.Combine(dataDir, "SECO_GLOBAL_ID.xlsx"));
var dbPath = Path.GetFullPath(Path.Combine(dataDir, "catalog.db"));

var store = new SecoGlobalIdStore(excelPath, dbPath);
store.Initialize();
Console.WriteLine($"Master list loaded: {store.Count} global IDs (excel={File.Exists(excelPath)})");

var client = new SecoApiClient(store);

// Master-list-only resolution (no link) — should resolve instantly to a global number.
string[] descriptions = ["C03509-T10P", "553055Z3.0-SIRON-A", "XOMX120408TR-ME08,F40M"];
foreach (var desc in descriptions)
{
    var record = new ToolRecord { ToolDescription = desc, ProcurementChannel = "SECO" };
    var resolved = store.TryResolve(desc, out var gid);
    var sw = Stopwatch.StartNew();
    var result = await client.FetchProductAsync(record);
    sw.Stop();
    Console.WriteLine($"[{desc}] masterList={resolved}({gid}) Success={result.Success} " +
        $"Props={result.Properties.Count} Item={result.ItemNumber} Elapsed={sw.Elapsed.TotalSeconds:F1}s Err={result.ErrorMessage}");
}
