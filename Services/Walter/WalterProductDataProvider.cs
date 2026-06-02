using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.Walter;

public class WalterProductDataProvider(IWalterApiClient apiClient) : IProductDataProvider
{
    public string Supplier => SupplierPrefixes.Walter;

    public Task<ProductFetchResult> FetchAsync(ToolRecord record, CancellationToken ct = default) =>
        apiClient.FetchProductAsync(record, ct);
}

