using AutoToolCatalog.Models;
using AutoToolCatalog.Services.Kennametal;
using AutoToolCatalog.Services.Seco;
using AutoToolCatalog.Services.Sandvik;
using AutoToolCatalog.Services.Walter;
using AutoToolCatalog.Services.TaeguTec;

namespace AutoToolCatalog.Services;

public class ProductDataProviderRegistry
{
    private readonly IReadOnlyList<IProductDataProvider> _providers;

    public ProductDataProviderRegistry(
        SecoProductDataProvider seco,
        KennametalProductDataProvider kennametal,
        SandvikProductDataProvider sandvik,
        WalterProductDataProvider walter,
        TaeguTecProductDataProvider taegutec)
    {
        _providers =
        [
            seco,
            kennametal,
            sandvik,
            walter,
            taegutec
        ];
    }

    public IProductDataProvider? GetProvider(string supplier)
    {
        var normalized = SupplierPrefixes.Normalize(supplier);
        return _providers.FirstOrDefault(p =>
            string.Equals(normalized, p.Supplier, StringComparison.OrdinalIgnoreCase));
    }
}
