using AutoToolCatalog.Models;
using AutoToolCatalog.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AutoToolCatalog.Services;

public class SignalRProgressReporter : IProgress<ProcessingProgress>
{
    private readonly IHubContext<ProcessingHub> _hub;
    private readonly string _sessionId;

    public SignalRProgressReporter(IHubContext<ProcessingHub> hub, string sessionId)
    {
        _hub = hub;
        _sessionId = sessionId;
    }

    public void Report(ProcessingProgress value)
    {
        _ = _hub.Clients.Group(_sessionId).SendAsync("ProgressUpdate", value);
    }
}
