using AutoToolCatalog.Models;
using AutoToolCatalog.Services.Seco;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHttpClient("SECO");
services.AddSingleton<ISecoApiClient, SecoApiClient>();
var client = services.BuildServiceProvider().GetRequiredService<ISecoApiClient>();

var record = new ToolRecord
{
    ToolDescription = "553055Z3.0-SIRON-A",
    ProcurementChannel = "SECO",
    WebpageLink = "https://www.secotools.com/article/p_02679365"
};

var result = await client.FetchProductAsync(record);
Console.WriteLine($"Success={result.Success} Error={result.ErrorMessage}");
Console.WriteLine($"Props={result.Properties.Count} Link={result.ProductUrl}");
