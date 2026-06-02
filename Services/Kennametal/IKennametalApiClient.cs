using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.Kennametal;

public interface IKennametalApiClient
{
    Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default);
}
