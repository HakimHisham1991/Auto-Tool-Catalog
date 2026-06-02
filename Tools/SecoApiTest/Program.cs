using AutoToolCatalog.Models;
using AutoToolCatalog.Services.Seco;
using Microsoft.Extensions.DependencyInjection;

if (args.Contains("--browser-only"))
{
    var bySearch = await SecoBrowserApiFetcher.FetchAsync(null, "SD1103-0300-014-06R1", null, CancellationToken.None);
    Console.WriteLine(bySearch == null
        ? "Search path: null"
        : $"Search path OK item={bySearch.ItemNumber} jsonLen={bySearch.Json.Length}");

    var byItem = await SecoBrowserApiFetcher.FetchAsync(null, "", "02898974", CancellationToken.None);
    Console.WriteLine(byItem == null
        ? "Direct item: null"
        : $"Direct item OK jsonLen={byItem.Json.Length}");
    return;
}

var services = new ServiceCollection();
services.AddHttpClient("SECO");
services.AddSingleton<ISecoApiClient, SecoApiClient>();
var client = services.BuildServiceProvider().GetRequiredService<ISecoApiClient>();

var fetch = await client.FetchProductAsync(new ToolRecord
{
    ToolDescription = "JH142040G2R100.0Z4-HXT",
    ProcurementChannel = "SECO"
});

Console.WriteLine($"Success={fetch.Success} Error={fetch.ErrorMessage}");
Console.WriteLine($"Props={fetch.Properties.Count} Link={fetch.ProductUrl}");
