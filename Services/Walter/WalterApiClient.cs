using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.Walter;

public partial class WalterApiClient : IWalterApiClient
{
    private const string WalterSite = "https://www.walter-tools.com";
    private const string Language = "en-gb";
    private const string MeasurementUnit = "Metric";

    private readonly IHttpClientFactory _httpClientFactory;

    public WalterApiClient(IHttpClientFactory httpClientFactory) =>
        _httpClientFactory = httpClientFactory;

    public async Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("WALTER");

            var productId = ResolveProductId(record);
            if (string.IsNullOrWhiteSpace(productId))
                return ProductFetchResult.Failed("Could not resolve Walter product ID from link or tool description");

            var productUrl = BuildProductUrl(productId, record.WebpageLink);
            var json = await GetProductJsonAsync(client, productId, productUrl, ct);
            if (string.IsNullOrWhiteSpace(json))
                return ProductFetchResult.Failed("Walter product API returned no data");

            if (!HasHits(json))
                return ProductFetchResult.Failed("Walter product not found");

            var properties = NormalizeFromJson(json);
            if (properties.Count == 0)
                return ProductFetchResult.Failed("Invalid or empty Walter API response (no properties)");

            return new ProductFetchResult
            {
                Success = true,
                ProductUrl = productUrl,
                ItemNumber = productId,
                RawJson = json,
                Properties = properties
            };
        }
        catch (Exception ex)
        {
            return ProductFetchResult.Failed(ex.Message);
        }
    }

    private static bool HasHits(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("hitCount", out var hitEl) || hitEl.ValueKind != JsonValueKind.Number)
                return true; // assume ok if field is missing
            return hitEl.TryGetInt32(out var hits) && hits > 0;
        }
        catch
        {
            return true; // invalid JSON handled elsewhere
        }
    }

    private static string? ResolveProductId(ToolRecord record)
    {
        var fromUrl = ExtractProductIdFromUrl(record.WebpageLink);
        if (!string.IsNullOrWhiteSpace(fromUrl))
            return fromUrl;

        if (string.IsNullOrWhiteSpace(record.ToolDescription))
            return null;

        // Walter ordering codes / IDs are typically the first token in the cell.
        // Examples: "DC180-05-05.500A1-WJ30EZ", "F2162-8", "A3289DPL-12", "H5035016-M12".
        var firstToken = record.ToolDescription.Trim()
            .Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstToken))
            return null;

        // Keep only the ID-ish token (Walter API expects lowercase).
        firstToken = firstToken.Trim();
        if (!LooksLikeWalterId(firstToken))
            return null;

        return firstToken.ToLowerInvariant();
    }

    internal static Dictionary<string, string> NormalizeFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("columns", out var columnsEl) || columnsEl.ValueKind != JsonValueKind.Array)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var firstItem = itemsEl.EnumerateArray().FirstOrDefault();
        if (firstItem.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var prefix = SupplierPrefixes.GetPropertyPrefix(SupplierPrefixes.Walter);
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var col in columnsEl.EnumerateArray())
        {
            if (col.ValueKind != JsonValueKind.Object)
                continue;

            var title = GetString(col, "title");
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var showInDetails = GetBool(col, "showInDetails");
            var showInList = GetBool(col, "showInList");
            if (!showInDetails && !showInList)
                continue;

            if (!TryGetPropertyCaseInsensitive(firstItem, title!, out var valueEl))
                continue;

            var unit = GetString(col, "unit");
            var decimals = GetInt(col, "decimalsMetric");

            var value = FormatValue(valueEl, decimals);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!string.IsNullOrWhiteSpace(unit) && !value.Contains(unit, StringComparison.OrdinalIgnoreCase))
                value = $"{value} {unit}".Trim();

            props[$"{prefix}{title.Trim()}"] = value;
        }

        return props;
    }

    private static async Task<string?> GetProductJsonAsync(HttpClient client, string productId, string referer, CancellationToken ct)
    {
        var url =
            $"{WalterSite}/api/productsearch/getproduct" +
            $"?id={Uri.EscapeDataString(productId)}" +
            $"&measurementUnit={Uri.EscapeDataString(MeasurementUnit)}" +
            $"&language={Uri.EscapeDataString(Language)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Referer", referer);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsStringAsync(ct);
    }

    private static string BuildProductUrl(string productId, string? existingLink)
    {
        if (!string.IsNullOrWhiteSpace(existingLink) &&
            existingLink.Contains(productId, StringComparison.OrdinalIgnoreCase))
            return existingLink;

        return $"{WalterSite}/{Language}/search/product/{Uri.EscapeDataString(productId)}";
    }

    private static string? ExtractProductIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var match = ProductPageRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string FormatValue(JsonElement value, int? decimals) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => FormatNumber(value, decimals),
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(v => FormatValue(v, decimals)).Where(v => !string.IsNullOrWhiteSpace(v))),
        _ => value.ToString()
    };

    private static string FormatNumber(JsonElement value, int? decimals)
    {
        if (value.TryGetInt64(out var i))
            return i.ToString(CultureInfo.InvariantCulture);

        var d = value.GetDouble();
        if (decimals is >= 0 and <= 10)
            return d.ToString($"F{decimals.Value}", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');

        return d.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            value = prop.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement obj, string name) =>
        TryGetPropertyCaseInsensitive(obj, name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool GetBool(JsonElement obj, string name) =>
        TryGetPropertyCaseInsensitive(obj, name, out var el) &&
        (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False) &&
        el.GetBoolean();

    private static int? GetInt(JsonElement obj, string name) =>
        TryGetPropertyCaseInsensitive(obj, name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)
            ? i
            : null;

    [GeneratedRegex(@"/search/product/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProductPageRegex();

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9.\-_/]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex WalterIdRegex();

    private static bool LooksLikeWalterId(string token) =>
        WalterIdRegex().IsMatch(token);
}

