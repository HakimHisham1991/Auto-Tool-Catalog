namespace AutoToolCatalog.Services.TaeguTec;

/// <summary>Runtime TaeguTec configuration resolved at startup (for clearer fetch error messages).</summary>
public sealed class TaeguTecRuntimeInfo
{
    public bool UsesBrowserbase { get; init; }
    public int CatalogEntryCount { get; init; }
    public string? CatalogExcelPath { get; init; }
}
