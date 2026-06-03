using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

/// <summary>
/// Placeholder for suppliers without live API integration (e.g. TAEGUTEC). Returns success with no properties so dynamic columns show #N/A.
/// </summary>
public class StubProductDataProvider(string supplier) : IProductDataProvider
{
    public string Supplier { get; } = supplier;

    public Task<ProductFetchResult> FetchAsync(ToolRecord record, CancellationToken ct = default) =>
        Task.FromResult(ProductFetchResult.NotAvailable(Supplier));
}
