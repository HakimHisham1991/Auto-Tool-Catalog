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
    /// Tool type for parsing logic. Only registered types are supported.
    /// </summary>
    public ToolType ToolType => InferToolType(TypeOfTool);

    public bool IsEndmill => ToolType == ToolType.SolidEndmill;
    public bool IsDrill => ToolType == ToolType.SolidDrill;
    public bool IsFacemill => ToolType == ToolType.Facemill;
    public bool IsInsertEndmill => ToolType == ToolType.InsertEndmill;

    /// <summary>
    /// True for Facemill or Insert Endmill (both use the same supplier rules).
    /// </summary>
    public bool IsFacemillOrInsertEndmill => IsFacemill || IsInsertEndmill;

    /// <summary>
    /// True only for registered/supported tool types.
    /// Unsupported types will not be scraped.
    /// </summary>
    public bool IsSupportedType => ToolType != ToolType.Unsupported;

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
        var upper = typeOfTool.Trim().ToUpperInvariant();

        // Strict matching: only registered types are supported.
        if (upper == "SOLID DRILL") return ToolType.SolidDrill;
        if (upper == "SOLID ENDMILL" || upper == "SOLID END MILL") return ToolType.SolidEndmill;
        if (upper == "FACEMILL" || upper == "FACE MILL") return ToolType.Facemill;
        if (upper == "INSERT ENDMILL" || upper == "INSERT END MILL") return ToolType.InsertEndmill;

        // Everything else is unsupported
        return ToolType.Unsupported;
    }
}

public enum ToolType
{
    Unsupported,
    SolidEndmill,
    SolidDrill,
    Facemill,
    InsertEndmill
}
