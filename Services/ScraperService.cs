using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

public class ScraperService : IScraperService
{
    private readonly IEnumerable<ISupplierParser> _parsers;
    private const int MaxConcurrency = 5;

    public ScraperService(IEnumerable<ISupplierParser> parsers)
    {
        _parsers = parsers;
    }

    public async Task<ProcessSession> ProcessAsync(ProcessSession session, IProgress<ProcessingProgress>? progress = null, CancellationToken ct = default)
    {
        var records = session.Records;
        var total = records.Count;
        var completed = 0;
        var successCount = 0;
        var failCount = 0;

        session.Progress = new ProcessingProgress { Total = total };

        var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = records.Select(async record =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                progress?.Report(new ProcessingProgress
                {
                    Total = total,
                    Completed = completed,
                    SuccessCount = successCount,
                    FailCount = failCount,
                    CurrentItem = record.ToolDescription
                });

                // Only process registered/supported tool types.
                // Unsupported types (Facemill, Insert Endmill, etc.) get all #NA.
                if (!record.IsSupportedType)
                {
                    ApplyResult(record, ToolSpecResult.AllNA());
                    Interlocked.Increment(ref successCount);
                }
                else
                {
                    var parser = GetParser(record.Supplier);
                    if (parser == null)
                    {
                        ApplyResult(record, ToolSpecResult.Failed($"Unknown supplier: {record.Supplier}"));
                        Interlocked.Increment(ref failCount);
                    }
                    else
                    {
                        var result = await parser.FetchSpecsAsync(record, ct);
                        ApplyResult(record, result);
                        if (result.Success || HasAnyValue(result))
                            Interlocked.Increment(ref successCount);
                        else
                            Interlocked.Increment(ref failCount);
                    }
                }

                var done = Interlocked.Increment(ref completed);
                progress?.Report(new ProcessingProgress
                {
                    Total = total,
                    Completed = done,
                    SuccessCount = successCount,
                    FailCount = failCount,
                    CurrentItem = done < records.Count ? records[done].ToolDescription : null
                });
                session.Progress.Completed = done;
                session.Progress.SuccessCount = successCount;
                session.Progress.FailCount = failCount;
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        session.Progress.Completed = completed;
        session.Progress.SuccessCount = successCount;
        session.Progress.FailCount = failCount;
        progress?.Report(session.Progress);
        return session;
    }

    private ISupplierParser? GetParser(string supplier)
    {
        return _parsers.FirstOrDefault(p =>
            string.Equals(p.SupplierName, supplier, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyResult(ToolRecord record, ToolSpecResult result)
    {
        record.ShankBoreDiameter = string.IsNullOrEmpty(record.ShankBoreDiameter) ? result.Spec6 : record.ShankBoreDiameter;
        record.ToolDiameter = string.IsNullOrEmpty(record.ToolDiameter) ? result.Spec1 : record.ToolDiameter;
        record.CornerRad = string.IsNullOrEmpty(record.CornerRad) ? result.Spec3 : record.CornerRad;
        record.FluteCuttingEdgeLength = string.IsNullOrEmpty(record.FluteCuttingEdgeLength) ? result.Spec2 : record.FluteCuttingEdgeLength;
        record.OverallLength = string.IsNullOrEmpty(record.OverallLength) ? result.Spec5 : record.OverallLength;
        record.PeripheralCuttingEdgeCount = string.IsNullOrEmpty(record.PeripheralCuttingEdgeCount) ? result.Spec4 : record.PeripheralCuttingEdgeCount;
    }

    private static bool HasAnyValue(ToolSpecResult r) =>
        !string.IsNullOrEmpty(r.Spec1) && r.Spec1 != "#NA" ||
        !string.IsNullOrEmpty(r.Spec2) && r.Spec2 != "#NA" ||
        !string.IsNullOrEmpty(r.Spec3) && r.Spec3 != "#NA" ||
        !string.IsNullOrEmpty(r.Spec4) && r.Spec4 != "#NA" ||
        !string.IsNullOrEmpty(r.Spec5) && r.Spec5 != "#NA" ||
        !string.IsNullOrEmpty(r.Spec6) && r.Spec6 != "#NA";
}
