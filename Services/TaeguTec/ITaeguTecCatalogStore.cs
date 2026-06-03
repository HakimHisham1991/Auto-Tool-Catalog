namespace AutoToolCatalog.Services.TaeguTec;

public interface ITaeguTecCatalogStore
{
    int Count { get; }
    bool TryResolve(string? toolDescription, out string catalogNo);
}
