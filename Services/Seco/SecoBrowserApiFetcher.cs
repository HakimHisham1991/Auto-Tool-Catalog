namespace AutoToolCatalog.Services.Seco;

/// <summary>
/// Resolves SECO item numbers and loads product JSON via shared Playwright pool.
/// </summary>
internal static class SecoBrowserApiFetcher
{
    public sealed record BrowserFetchResult(string ItemNumber, string ProductUrl, string Json);

    public static async Task<BrowserFetchResult?> FetchAsync(
        string? webpageLink,
        string designation,
        string? knownItemNumber,
        CancellationToken ct)
    {
        var result = await SecoPlaywrightPool.FetchAsync(webpageLink, designation, knownItemNumber, ct);
        return result == null
            ? null
            : new BrowserFetchResult(result.ItemNumber, result.ProductUrl, result.Json);
    }
}
