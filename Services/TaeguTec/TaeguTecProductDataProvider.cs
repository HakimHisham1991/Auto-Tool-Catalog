using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.TaeguTec;

public class TaeguTecProductDataProvider(ITaeguTecApiClient apiClient) : IProductDataProvider
{
    public string Supplier => SupplierPrefixes.TaeguTec;

    public Task<ProductFetchResult> FetchAsync(ToolRecord record, CancellationToken ct = default) =>
        apiClient.FetchProductAsync(record, ct);
}
