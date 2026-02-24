using System.Text.Json;

namespace MyPalClara.Modules.Sdk;

public interface IGatewayBridge
{
    Task SendToNodeAsync(string nodeId, object message, CancellationToken ct = default);
    Task BroadcastToPlatformAsync(string platform, object message, CancellationToken ct = default);
    void OnProtocolMessage(string messageType, Func<string, JsonElement, Task> handler);
    IReadOnlyList<ConnectedNode> GetConnectedNodes();
}

public record ConnectedNode(
    string NodeId,
    string Platform,
    string SessionId,
    List<string> Capabilities,
    DateTime ConnectedAt);
