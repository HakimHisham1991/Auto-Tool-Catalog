using AutoToolCatalog.Models;
using AutoToolCatalog.Services.Seco;
using Microsoft.Extensions.Configuration;

var dataDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Data"));
var store = new SecoGlobalIdStore(
    Path.Combine(dataDir, "SECO_GLOBAL_ID.xlsx"),
    Path.Combine(dataDir, "catalog.db"));
store.Initialize();

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Seco:Market"] = "MY",
        ["Seco:Language"] = "en-GB"
    })
    .Build();

using var session = new SecoHttpSession(config);
var client = new SecoApiClient(session, store);

string[] items =
[
    "JS755100E3R050.0Z5-HXT",
    "SD205A-0330-021-06R1-MS",
    "JH142040G2R100.0Z4-HXT",
    "JH724100T2R2R030.0Z4",
    "SD1105A-1180-056-12R1"
];

foreach (var desc in items)
{
    store.TryResolve(desc, out var gid);
    var result = await client.FetchProductAsync(new ToolRecord
    {
        ToolDescription = desc,
        ProcurementChannel = "SECO"
    });
    Console.WriteLine($"[{desc}] master={gid} ok={result.Success} item={result.ItemNumber} err={result.ErrorMessage}");
}
