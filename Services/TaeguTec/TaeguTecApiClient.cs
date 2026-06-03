using System.Text.Json;
using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services.TaeguTec;

public class TaeguTecApiClient : ITaeguTecApiClient
{
    private readonly ITaeguTecItemFetcher _fetcher;
    private readonly ITaeguTecCatalogStore _catalogStore;
    private readonly TaeguTecRuntimeInfo _runtime;

    public TaeguTecApiClient(
        ITaeguTecItemFetcher fetcher,
        ITaeguTecCatalogStore catalogStore,
        TaeguTecRuntimeInfo runtime)
    {
        _fetcher = fetcher;
        _catalogStore = catalogStore;
        _runtime = runtime;
    }

    public async Task<ProductFetchResult> FetchProductAsync(ToolRecord record, CancellationToken ct = default)
    {
        try
        {
            var catalogNo = ResolveCatalogNoLocally(record);
            if (string.IsNullOrWhiteSpace(catalogNo))
            {
                if (_catalogStore.Count == 0)
                {
                    return ProductFetchResult.Failed(
                        "TaeguTec: master catalog is empty — ensure Data/TAEGUTEC_CATALOG_NO.xlsx is deployed " +
                        $"({_runtime.CatalogExcelPath ?? "path unknown"})");
                }

                return ProductFetchResult.Failed(
                    "TaeguTec: cannot determine catalog number from Link, master list, or Tool Description");
            }

            var dto = await _fetcher.FetchItemAsync(catalogNo, record.WebpageLink, ct);
            if (dto == null)
            {
                var detail = _fetcher.LastError;
                if (string.IsNullOrWhiteSpace(detail) && !_runtime.UsesBrowserbase)
                {
                    detail = "Plain HTTP fetch failed (IMC is Cloudflare-protected). " +
                               "Set BROWSERBASE_API_KEY (env var) or TaeguTec:BrowserbaseApiKey in appsettings.Production.local.json.";
                }

                return ProductFetchResult.Failed(
                    string.IsNullOrWhiteSpace(detail)
                        ? $"TaeguTec: no data returned for cat={catalogNo}"
                        : $"TaeguTec: {detail} (cat={catalogNo})");
            }

            if (dto.Parameters.Count == 0)
                return ProductFetchResult.Failed($"TaeguTec: empty parameters for cat={catalogNo}");

            var prefix = SupplierPrefixes.GetPropertyPrefix(SupplierPrefixes.TaeguTec);
            var properties = dto.Parameters.ToDictionary(
                kvp => $"{prefix}{kvp.Key}",
                kvp => kvp.Value,
                StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(dto.ItemDesignation))
                properties[$"{prefix}DESIGNATION"] = dto.ItemDesignation.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Grade))
                properties[$"{prefix}GRADE"] = dto.Grade.Trim();
            if (!string.IsNullOrWhiteSpace(dto.FamilyDesignation))
                properties[$"{prefix}FAMILY"] = dto.FamilyDesignation.Trim();
            if (!string.IsNullOrWhiteSpace(dto.FamilyDescription))
                properties[$"{prefix}FAMILY_DESC"] = dto.FamilyDescription.Trim();
            if (!string.IsNullOrWhiteSpace(dto.ItemsPerPackage))
                properties[$"{prefix}ITEMS_PER_PACKAGE"] = dto.ItemsPerPackage.Trim();

            var fnum = ExtractFromLink(record.WebpageLink, "fnum");
            var mapp = ExtractFromLink(record.WebpageLink, "mapp");
            var productUrl = !string.IsNullOrWhiteSpace(record.WebpageLink) &&
                             record.WebpageLink.Contains("item.aspx", StringComparison.OrdinalIgnoreCase)
                ? record.WebpageLink
                : TaeguTecHttpSession.BuildItemUrl(catalogNo, fnum, mapp);

            return new ProductFetchResult
            {
                Success = true,
                ProductUrl = productUrl,
                ItemNumber = catalogNo,
                RawJson = JsonSerializer.Serialize(properties),
                Properties = properties
            };
        }
        catch (Exception ex)
        {
            return ProductFetchResult.Failed(ex.Message);
        }
    }

    private string? ResolveCatalogNoLocally(ToolRecord record)
    {
        var fromLink = TaeguTecHttpSession.ExtractCatalogNo(record.WebpageLink, null);
        if (!string.IsNullOrWhiteSpace(fromLink))
            return fromLink;

        if (_catalogStore.TryResolve(record.ToolDescription, out var fromMasterList) &&
            !string.IsNullOrWhiteSpace(fromMasterList))
            return fromMasterList;

        return TaeguTecHttpSession.ExtractCatalogNo(null, record.ToolDescription);
    }

    private static string? ExtractFromLink(string? link, string param)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            link, $@"[?&]{param}=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
}
