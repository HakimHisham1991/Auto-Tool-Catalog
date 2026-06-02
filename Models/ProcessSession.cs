namespace AutoToolCatalog.Models;

public class ProcessSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? SourceFileName { get; set; }
    public List<ToolRecord> Records { get; set; } = new();
    public ProcessingProgress Progress { get; set; } = new();
    public CancellationTokenSource? Cts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Union of all dynamic property column keys across records in this session.
    /// </summary>
    public List<string> PropertyColumns { get; set; } = new();
}
