using Microsoft.AspNetCore.SignalR;

namespace MyPalClara.Web.Hubs;

/// <summary>
/// SignalR hub for real-time dashboard updates.
/// Client methods: ReceiveLogEntry, SessionUpdated, MemoryUpdated.
/// </summary>
public sealed class DashboardHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("ReceiveLogEntry", new
        {
            timestamp = DateTime.UtcNow,
            level = "info",
            message = "Connected to dashboard hub",
        });
        await base.OnConnectedAsync();
    }
}
