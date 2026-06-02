using AutoToolCatalog.Data;
using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

public class ScraperService : IScraperService
{
    private readonly ProductDataProviderRegistry _providers;
    private readonly ICatalogRepository _catalog;
    private const int MaxConcurrency = 5;
    private const string NotAvailable = "#N/A";

    public ScraperService(ProductDataProviderRegistry providers, ICatalogRepository catalog)
    {
        _providers = providers;
        _catalog = catalog;
    }

    public async Task<ProcessSession> ProcessAsync(
        ProcessSession session,
        IProgress<ProcessingProgress>? progress = null,
        Func<int, ToolRecord, Task>? onRecordCompleted = null,
        CancellationToken ct = default)
    {
        var records = session.Records;
        var total = records.Count;
        var completed = 0;
        var successCount = 0;
        var failCount = 0;

        session.Progress = new ProcessingProgress { Total = total };
        session.PropertyColumns = new List<string>();

        var semaphore = new SemaphoreSlim(MaxConcurrency);
        var columnLock = new object();

        var tasks = records.Select(async (record, index) =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();

                progress?.Report(new ProcessingProgress
                {
                    Total = total,
                    Completed = completed,
                    SuccessCount = successCount,
                    FailCount = failCount,
                    CurrentItem = record.ToolDescription
                });

                var fetchResult = await FetchRecordAsync(session.Id, index, record, ct);
                ApplyFetchResult(record, fetchResult, session, columnLock);

                if (!fetchResult.Success)
                    Interlocked.Increment(ref failCount);
                else if (SupplierPrefixes.IsApiSupported(record.Supplier) && fetchResult.Properties.Count == 0)
                    Interlocked.Increment(ref failCount);
                else
                    Interlocked.Increment(ref successCount);

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

                if (onRecordCompleted != null)
                {
                    try { await onRecordCompleted(index, record); } catch { /* ignore SignalR errors */ }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // stopped by user
        }

        session.PropertyColumns.Sort(StringComparer.OrdinalIgnoreCase);
        session.Progress.Completed = completed;
        session.Progress.SuccessCount = successCount;
        session.Progress.FailCount = failCount;
        session.Progress.IsStopped = ct.IsCancellationRequested;
        progress?.Report(session.Progress);
        return session;
    }

    private async Task<ProductFetchResult> FetchRecordAsync(string sessionId, int index, ToolRecord record, CancellationToken ct)
    {
        var provider = _providers.GetProvider(record.Supplier);
        if (provider == null)
            return ProductFetchResult.Failed($"Unknown supplier: {record.Supplier}");

        var result = await provider.FetchAsync(record, ct);

        if (!string.IsNullOrWhiteSpace(result.ProductUrl))
            record.WebpageLink = result.ProductUrl;

        if (!string.IsNullOrWhiteSpace(result.RawJson))
            _catalog.SaveRawProduct(sessionId, index, record.Supplier, result.ProductUrl, result.ItemNumber, result.RawJson);

        if (result.Properties.Count > 0)
            _catalog.SaveAttributes(sessionId, index, result.Properties);

        return result;
    }

    private static void ApplyFetchResult(
        ToolRecord record,
        ProductFetchResult result,
        ProcessSession session,
        object columnLock)
    {
        record.Properties.Clear();

        lock (columnLock)
        {
            foreach (var column in session.PropertyColumns)
                record.Properties[column] = NotAvailable;

            foreach (var (key, value) in result.Properties)
            {
                record.Properties[key] = value;
                if (!session.PropertyColumns.Contains(key, StringComparer.OrdinalIgnoreCase))
                    session.PropertyColumns.Add(key);
            }

            if (!SupplierPrefixes.IsApiSupported(record.Supplier))
            {
                foreach (var column in session.PropertyColumns)
                {
                    if (!record.Properties.ContainsKey(column))
                        record.Properties[column] = NotAvailable;
                }
            }
        }
    }
}
