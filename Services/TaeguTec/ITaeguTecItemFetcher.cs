using AutoToolCatalog.Models.TaeguTec;

namespace AutoToolCatalog.Services.TaeguTec;

/// <summary>
/// Fetches a parsed TaeguTec item. Implemented by the plain-HTTP session and the Browserbase (cloud browser) fetcher.
/// </summary>
public interface ITaeguTecItemFetcher
{
    string? LastError { get; }
    Task<TaeguTecItemDto?> FetchItemAsync(string catalogNo, string? knownItemUrl, CancellationToken ct = default);
}
