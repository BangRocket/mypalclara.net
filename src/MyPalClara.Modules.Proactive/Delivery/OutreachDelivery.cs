using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Proactive.Delivery;

public class OutreachDelivery
{
    private readonly IGatewayBridge _bridge;
    private readonly ILogger<OutreachDelivery> _logger;

    public OutreachDelivery(IGatewayBridge bridge, ILogger<OutreachDelivery> logger)
    {
        _bridge = bridge;
        _logger = logger;
    }

    public async Task SendAsync(string userId, string message, CancellationToken ct = default)
    {
        var payload = new
        {
            type = "proactive_message",
            user_id = userId,
            content = message,
            timestamp = DateTime.UtcNow.ToString("O")
        };

        // Broadcast to all platforms -- adapter decides how to route
        var nodes = _bridge.GetConnectedNodes();
        foreach (var node in nodes)
        {
            try
            {
                await _bridge.SendToNodeAsync(node.NodeId, payload, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deliver proactive message to node {Node}", node.NodeId);
            }
        }

        _logger.LogInformation("Delivered proactive message to user {User}", userId);
    }
}
