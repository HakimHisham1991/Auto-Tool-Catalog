namespace AutoToolCatalog.Models;

public static class SupplierPrefixes
{
    public const string Seco = "SECO";
    public const string Kennametal = "KENNAMETAL";
    public const string Sandvik = "SANDVIK";
    public const string Walter = "WALTER";

    public static string Normalize(string channel)
    {
        var upper = channel.ToUpperInvariant();
        if (upper.Contains("SECO")) return Seco;
        if (upper.Contains("KENNAMETAL")) return Kennametal;
        if (upper.Contains("SANDVIK")) return Sandvik;
        if (upper.Contains("WALTER")) return Walter;
        return upper.Trim();
    }

    public static string GetPropertyPrefix(string supplier) => supplier switch
    {
        Seco => "SECO_",
        Kennametal => "KENN_",
        Sandvik => "SAND_",
        Walter => "WALT_",
        _ => $"{supplier}_"
    };

    public static bool IsApiSupported(string supplier) =>
        supplier.Equals(Seco, StringComparison.OrdinalIgnoreCase) ||
        supplier.Equals(Kennametal, StringComparison.OrdinalIgnoreCase);
}
