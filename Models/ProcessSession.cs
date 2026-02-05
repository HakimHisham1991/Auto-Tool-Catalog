namespace AutoToolCatalog.Models;

/// <summary>
/// In-memory session holding tool records and progress.
/// </summary>
public class ProcessSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<ToolRecord> Records { get; set; } = new();
    public ProcessingProgress Progress { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
