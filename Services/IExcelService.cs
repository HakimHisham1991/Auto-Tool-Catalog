using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

public interface IExcelService
{
    Task<List<ToolRecord>> ImportAsync(Stream stream, CancellationToken ct = default);
    Task<byte[]> ExportAsync(List<ToolRecord> records, IReadOnlyList<string> propertyColumns, CancellationToken ct = default);
}
