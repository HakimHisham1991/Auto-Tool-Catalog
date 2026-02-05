using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

/// <summary>
/// Parser for a specific supplier website. Fetches tool specs by search or direct URL.
/// </summary>
public interface ISupplierParser
{
    string SupplierName { get; }

    Task<ToolSpecResult> FetchSpecsAsync(ToolRecord record, CancellationToken ct = default);
}
