using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

/// <summary>
/// Placeholder for suppliers without API integration yet.
/// </summary>
public class StubProductDataProvider(string supplier) : IProductDataProvider
{
    public string Supplier { get; } = supplier;

    public Task<ProductFetchResult> FetchAsync(ToolRecord record, CancellationToken ct = default) =>
        Task.FromResult(ProductFetchResult.NotAvailable(Supplier));
}
