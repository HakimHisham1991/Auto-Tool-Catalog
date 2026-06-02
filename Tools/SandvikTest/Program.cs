using AutoToolCatalog.Models;
using AutoToolCatalog.Services.Sandvik;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHttpClient("SANDVIK", c =>
{
    c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
    c.DefaultRequestHeaders.Add("Accept", "application/json");
});
services.AddSingleton<ISandvikApiClient, SandvikApiClient>();
var sp = services.BuildServiceProvider();
var client = sp.GetRequiredService<ISandvikApiClient>();

var rows = new[]
{
    "R216.42-06030-AK10G 1610",
    "1P260-0150-XA 1620",
    "2S340-0600-100-MA 1640",
    "316-12SM450-12015P 1030",
    "316-16CM800-16045G 1030"
};

foreach (var desc in rows)
{
    var result = await client.FetchProductAsync(new ToolRecord { ToolDescription = desc, ProcurementChannel = "SANDVIK" });
    Console.WriteLine($"{desc}: success={result.Success} props={result.Properties.Count} id={result.ItemNumber} err={result.ErrorMessage}");
}
