using System.Collections.Concurrent;
using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

public class ProcessSessionStore : IProcessSessionStore
{
    private readonly ConcurrentDictionary<string, ProcessSession> _sessions = new();

    public ProcessSession? Get(string id) => _sessions.TryGetValue(id, out var s) ? s : null;

    public void Set(ProcessSession session) => _sessions[session.Id] = session;
}
