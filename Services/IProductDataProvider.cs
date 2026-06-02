using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

public interface IProductDataProvider
{
    string Supplier { get; }
    Task<ProductFetchResult> FetchAsync(ToolRecord record, CancellationToken ct = default);
}
