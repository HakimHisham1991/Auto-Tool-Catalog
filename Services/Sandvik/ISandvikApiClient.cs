using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.Sandvik;

public interface ISandvikApiClient
{
    Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default);
}
