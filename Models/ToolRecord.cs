namespace AutoToolCatalog.Models;

/// <summary>
/// Represents a single row from the tooling Excel database.
/// </summary>
public class ToolRecord
{
    public int RowIndex { get; set; }

    public int No { get; set; }
    public string ToolDescription { get; set; } = string.Empty;
    public string TypeOfTool { get; set; } = string.Empty;
    public string? ShankBoreDiameter { get; set; }
    public string? ToolDiameter { get; set; }
    public string? CornerRad { get; set; }
    public string? FluteCuttingEdgeLength { get; set; }
    public string? OverallLength { get; set; }
    public string? PeripheralCuttingEdgeCount { get; set; }
    public string ProcurementChannel { get; set; } = string.Empty;

    /// <summary>
    /// Normalized supplier name (SECO, KENNAMETAL, SANDVIK, WALTER).
    /// </summary>
    public string Supplier => NormalizeSupplier(ProcurementChannel);

    /// <summary>
    /// Tool type for parsing logic: Endmill or Drill.
    /// </summary>
    public ToolType ToolType => InferToolType(TypeOfTool);

    public bool IsEndmill => ToolType == ToolType.Endmill;
    public bool IsDrill => ToolType == ToolType.Drill;

    private static string NormalizeSupplier(string channel)
    {
        var upper = channel.ToUpperInvariant();
        if (upper.Contains("SECO")) return "SECO";
        if (upper.Contains("KENNAMETAL")) return "KENNAMETAL";
        if (upper.Contains("SANDVIK")) return "SANDVIK";
        if (upper.Contains("WALTER")) return "WALTER";
        return channel;
    }

    private static ToolType InferToolType(string typeOfTool)
    {
        var upper = typeOfTool.ToUpperInvariant();
        if (upper.Contains("DRILL")) return ToolType.Drill;
        if (upper.Contains("ENDMILL") || upper.Contains("END MILL") || upper.Contains("MILL")) return ToolType.Endmill;
        return ToolType.Endmill;
    }
}

public enum ToolType
{
    Endmill,
    Drill
}
