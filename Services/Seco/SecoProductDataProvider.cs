using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.Seco;

public class SecoProductDataProvider(ISecoApiClient apiClient) : IProductDataProvider
{
    public string Supplier => SupplierPrefixes.Seco;

    public Task<ProductFetchResult> FetchAsync(ToolRecord record, CancellationToken ct = default) =>
        apiClient.FetchProductAsync(record, ct);
}
