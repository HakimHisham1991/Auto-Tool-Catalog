namespace AutoToolCatalog.Models;

/// <summary>
/// Core tooling row from Excel plus dynamically discovered supplier properties.
/// </summary>
public class ToolRecord
{
    public int RowIndex { get; set; }
    public int No { get; set; }
    public string ToolDescription { get; set; } = string.Empty;
    public string ProcurementChannel { get; set; } = string.Empty;
    public string? WebpageLink { get; set; }

    /// <summary>
    /// Normalized supplier key (SECO, KENNAMETAL, SANDVIK, WALTER).
    /// </summary>
    public string Supplier => SupplierPrefixes.Normalize(ProcurementChannel);

    /// <summary>
    /// Dynamic columns keyed as SUPPLIER_ATTRIBUTE (e.g. SECO_DC).
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
