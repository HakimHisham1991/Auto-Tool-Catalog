namespace AutoToolCatalog.Models;

public class ProductFetchResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProductUrl { get; set; }
    public string? ItemNumber { get; set; }
    public string? RawJson { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static ProductFetchResult Failed(string message) => new() { Success = false, ErrorMessage = message };

    public static ProductFetchResult NotAvailable(string supplier) => new()
    {
        Success = true,
        ErrorMessage = $"{supplier} API not implemented yet"
    };
}
