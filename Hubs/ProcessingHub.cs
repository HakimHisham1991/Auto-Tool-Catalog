using AutoToolCatalog.Models;
using Microsoft.AspNetCore.SignalR;

namespace AutoToolCatalog.Hubs;

public class ProcessingHub : Hub
{
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }
}
