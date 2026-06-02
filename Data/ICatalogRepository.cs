namespace AutoToolCatalog.Data;

public interface ICatalogRepository
{
    void Initialize();
    void SaveRawProduct(string sessionId, int recordIndex, string supplier, string? productUrl, string? itemNumber, string rawJson);
    void SaveAttributes(string sessionId, int recordIndex, IReadOnlyDictionary<string, string> attributes);
    IReadOnlyDictionary<string, string> GetAttributes(string sessionId, int recordIndex);
}
