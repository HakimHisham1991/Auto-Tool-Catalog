using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.Sandvik;

public class SandvikProductDataProvider(ISandvikApiClient apiClient) : IProductDataProvider
{
    public string Supplier => SupplierPrefixes.Sandvik;

    public Task<ProductFetchResult> FetchAsync(ToolRecord record, CancellationToken ct = default) =>
        apiClient.FetchProductAsync(record, ct);
}
