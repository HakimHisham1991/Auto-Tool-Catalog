using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.Kennametal;

public class KennametalProductDataProvider(IKennametalApiClient apiClient) : IProductDataProvider
{
    public string Supplier => SupplierPrefixes.Kennametal;

    public Task<ProductFetchResult> FetchAsync(ToolRecord record, CancellationToken ct = default) =>
        apiClient.FetchProductAsync(record, ct);
}
