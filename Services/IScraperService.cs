using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

public interface IScraperService
{
    Task<ProcessSession> ProcessAsync(ProcessSession session, IProgress<ProcessingProgress>? progress = null, CancellationToken ct = default);
}
