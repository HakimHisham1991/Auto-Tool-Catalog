using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

public interface IProcessSessionStore
{
    ProcessSession? Get(string id);
    void Set(ProcessSession session);
}
