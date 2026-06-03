namespace AutoToolCatalog.Models.TaeguTec;

public class TaeguTecItemDto
{
    public string CatalogNo { get; set; } = "";
    public string ItemDesignation { get; set; } = "";
    public string FamilyDesignation { get; set; } = "";
    public string FamilyDescription { get; set; } = "";
    public string Grade { get; set; } = "";
    public string ItemsPerPackage { get; set; } = "";
    public string FamilyRemarks { get; set; } = "";
    public string? ImageUrl2D { get; set; }

    /// <summary>ISO13399 parameter codes (e.g. DC, OAL) → values.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
