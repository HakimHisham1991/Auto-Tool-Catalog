using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.TaeguTec;

public interface ITaeguTecApiClient
{
    Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default);
}
