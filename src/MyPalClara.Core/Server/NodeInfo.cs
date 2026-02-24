using System.Net.WebSockets;

namespace MyPalClara.Core.Server;

/// <summary>
/// Tracks a connected adapter node.
/// </summary>
public class NodeInfo
{
    public required string NodeId { get; set; }
    public required string Platform { get; set; }
    public required WebSocket WebSocket { get; set; }
    public required string SessionId { get; set; }
    public List<string> Capabilities { get; set; } = [];
    public Dictionary<string, object> Metadata { get; set; } = [];
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastPingAt { get; set; } = DateTime.UtcNow;
}
