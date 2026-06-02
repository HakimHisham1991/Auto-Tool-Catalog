using AutoToolCatalog.Models;
using AutoToolCatalog.Services.Kennametal;
using AutoToolCatalog.Services.Seco;

namespace AutoToolCatalog.Services;

public class ProductDataProviderRegistry
{
    private readonly IReadOnlyList<IProductDataProvider> _providers;

    public ProductDataProviderRegistry(SecoProductDataProvider seco, KennametalProductDataProvider kennametal)
    {
        _providers =
        [
            seco,
            kennametal,
            new StubProductDataProvider(SupplierPrefixes.Sandvik),
            new StubProductDataProvider(SupplierPrefixes.Walter)
        ];
    }

    public IProductDataProvider? GetProvider(string supplier)
    {
        var normalized = SupplierPrefixes.Normalize(supplier);
        return _providers.FirstOrDefault(p =>
            string.Equals(normalized, p.Supplier, StringComparison.OrdinalIgnoreCase));
    }
}
