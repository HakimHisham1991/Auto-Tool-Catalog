namespace AutoToolCatalog.Models;

/// <summary>
/// Result of scraping tool specs from a supplier website.
/// Spec1–Spec6 map to Excel columns per supplier mapping.
/// </summary>
public class ToolSpecResult
{
    public string Spec1 { get; set; } = "#NA"; // Tool Ø / DC / D1
    public string Spec2 { get; set; } = "#NA"; // Flute length / APMX / LU
    public string Spec3 { get; set; } = "#NA"; // Corner rad / RE
    public string Spec4 { get; set; } = "#NA"; // Peripheral cutting edge count / Z
    public string Spec5 { get; set; } = "#NA"; // Overall length / OAL / LF
    public string Spec6 { get; set; } = "#NA"; // Shank Ø / Bore Ø / DMM / DCONMS

    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static ToolSpecResult Failed(string message) => new() { Success = false, ErrorMessage = message };
}
