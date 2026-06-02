using AutoToolCatalog.Models;
using AutoToolCatalog.Services;
using AutoToolCatalog.Services.TaeguTec;
using Microsoft.Extensions.DependencyInjection;

var excelPath = args.Length > 0
    ? args[0]
    : @"c:\Users\Public\Documents\Auto-Tool-Catalog\TEST_SAMPLE\Tool Database_Test_5_Taegutec.xlsx";

var services = new ServiceCollection();
services.AddHttpClient("TAEGUTEC", c =>
{
    c.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    c.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
});
services.AddScoped<IExcelService, ExcelService>();
services.AddScoped<ITaeguTecApiClient, TaeguTecApiClient>();

await using var sp = services.BuildServiceProvider();
var excel = sp.GetRequiredService<IExcelService>();
var client = sp.GetRequiredService<ITaeguTecApiClient>();

await using var stream = File.OpenRead(excelPath);
var records = await excel.ImportAsync(stream);
Console.WriteLine($"Imported {records.Count} rows from {Path.GetFileName(excelPath)}\n");

foreach (var record in records)
{
    Console.WriteLine($"--- Row {record.RowIndex}: {record.ToolDescription}");
    Console.WriteLine($"    Supplier: {record.ProcurementChannel}");
    Console.WriteLine($"    Link: {record.WebpageLink ?? "(none)"}");
    Console.WriteLine($"    Normalized: {SupplierPrefixes.Normalize(record.ProcurementChannel)}");

    var result = await client.FetchProductAsync(record);
    Console.WriteLine($"    Success={result.Success} Props={result.Properties.Count}");
    if (!result.Success)
        Console.WriteLine($"    Error: {result.ErrorMessage}");
    else
    {
        Console.WriteLine($"    URL: {result.ProductUrl}");
        foreach (var kv in result.Properties.Take(8))
            Console.WriteLine($"      {kv.Key}={kv.Value}");
        if (result.Properties.Count > 8)
            Console.WriteLine($"      ... +{result.Properties.Count - 8} more");
    }
    Console.WriteLine();
}
