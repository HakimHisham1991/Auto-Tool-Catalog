using AutoToolCatalog.Models;
using AutoToolCatalog.Services.Kennametal;
using AutoToolCatalog.Services.Seco;
using AutoToolCatalog.Services.Sandvik;
using AutoToolCatalog.Services.Walter;

namespace AutoToolCatalog.Services;

public class ProductDataProviderRegistry
{
    private readonly IReadOnlyList<IProductDataProvider> _providers;

    public ProductDataProviderRegistry(
        SecoProductDataProvider seco,
        KennametalProductDataProvider kennametal,
        SandvikProductDataProvider sandvik,
        WalterProductDataProvider walter)
    {
        _providers =
        [
            seco,
            kennametal,
            sandvik,
            walter,
            new StubProductDataProvider(SupplierPrefixes.TaeguTec)
        ];
    }

    public IProductDataProvider? GetProvider(string supplier)
    {
        var normalized = SupplierPrefixes.Normalize(supplier);
        return _providers.FirstOrDefault(p =>
            string.Equals(normalized, p.Supplier, StringComparison.OrdinalIgnoreCase));
    }
}
