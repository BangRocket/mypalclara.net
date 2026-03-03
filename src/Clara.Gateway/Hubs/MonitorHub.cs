using Microsoft.AspNetCore.SignalR;

namespace Clara.Gateway.Hubs;

public interface IMonitorClient
{
    Task ReceiveEvent(string eventType, string data);
    Task ReceiveMetrics(string metricsJson);
}

public class MonitorHub : Hub<IMonitorClient>
{
}
