namespace AutoToolCatalog.Models;

/// <summary>
/// Real-time progress for the processing job.
/// </summary>
public class ProcessingProgress
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public string? CurrentItem { get; set; }
    public int PercentComplete => Total > 0 ? (Completed * 100) / Total : 0;
    public bool IsFinished => Completed >= Total;
}
