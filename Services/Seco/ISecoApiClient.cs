using AutoToolCatalog.Models;
using AutoToolCatalog.Models.Seco;

namespace AutoToolCatalog.Services.Seco;

public interface ISecoApiClient
{
    Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default);
}
