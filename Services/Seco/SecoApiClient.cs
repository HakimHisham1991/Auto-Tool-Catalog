using System.Text.Json;
using AutoToolCatalog.Models;
using AutoToolCatalog.Models.Seco;

namespace AutoToolCatalog.Services.Seco;

public class SecoApiClient : ISecoApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ISecoGlobalIdStore? _globalIdStore;

    public SecoApiClient(ISecoGlobalIdStore? globalIdStore = null) =>
        _globalIdStore = globalIdStore;

    public async Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default)
    {
        try
        {
            var itemNumber = ResolveItemNumberLocally(record);

            // Only hit the network for resolution when the local master list / link / description can't.
            if (string.IsNullOrWhiteSpace(itemNumber))
            {
                itemNumber = await SecoHttpSession.Default.ResolveItemNumberAsync(
                    record.WebpageLink, record.ToolDescription, ct);
            }

            string? productUrl = null;
            string? json = null;

            var browser = await SecoPlaywrightPool.FetchAsync(
                record.WebpageLink, record.ToolDescription ?? string.Empty, itemNumber, ct);
            if (browser != null)
            {
                itemNumber = browser.ItemNumber;
                productUrl = browser.ProductUrl;
                json = browser.Json;
            }

            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(itemNumber))
                return ProductFetchResult.Failed("Could not resolve SECO item number or product data from link, description, or search");

            productUrl ??= $"https://www.secotools.com/article/p_{itemNumber}";

            var product = JsonSerializer.Deserialize<SecoProductDto>(json, JsonOptions);
            var properties = product == null ? new Dictionary<string, string>() : NormalizeAttributes(product);
            if (properties.Count == 0)
                return ProductFetchResult.Failed("Invalid or empty SECO API response (no attributes)");

            return new ProductFetchResult
            {
                Success = true,
                ProductUrl = productUrl,
                ItemNumber = product?.ItemNumber ?? itemNumber,
                RawJson = json,
                Properties = properties
            };
        }
        catch (Exception ex)
        {
            return ProductFetchResult.Failed(ex.Message);
        }
    }

    private string? ResolveItemNumberLocally(ToolRecord record)
    {
        var fromLink = SecoHttpSession.ExtractItemNumberFromUrl(record.WebpageLink);
        if (!string.IsNullOrWhiteSpace(fromLink))
            return fromLink;

        if (_globalIdStore != null &&
            _globalIdStore.TryResolve(record.ToolDescription, out var fromMasterList) &&
            !string.IsNullOrWhiteSpace(fromMasterList))
            return fromMasterList;

        return null;
    }

    internal static Dictionary<string, string> NormalizeAttributes(SecoProductDto product)
    {
        var prefix = SupplierPrefixes.GetPropertyPrefix(SupplierPrefixes.Seco);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attr in product.Attributes)
        {
            var valueText = attr.ValueText;
            if (string.IsNullOrWhiteSpace(attr.Name) || string.IsNullOrWhiteSpace(valueText))
                continue;

            var value = valueText.Trim();
            if (!string.IsNullOrWhiteSpace(attr.Unit) && !value.Contains(attr.Unit, StringComparison.OrdinalIgnoreCase))
                value = $"{value} {attr.Unit}".Trim();

            if (!string.IsNullOrWhiteSpace(attr.ValueDescription) &&
                attr.ValueDescription != value &&
                !value.Contains(attr.ValueDescription, StringComparison.OrdinalIgnoreCase))
                value = $"{value} ({attr.ValueDescription.Trim()})";

            properties[$"{prefix}{attr.Name.Trim()}"] = value;
        }

        return properties;
    }
}
