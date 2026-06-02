using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.Walter;

public interface IWalterApiClient
{
    Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default);
}

