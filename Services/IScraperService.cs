using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

public interface IScraperService
{
    Task<ProcessSession> ProcessAsync(ProcessSession session, IProgress<ProcessingProgress>? progress = null, Func<int, ToolRecord, Task>? onRecordCompleted = null, CancellationToken ct = default);
}
