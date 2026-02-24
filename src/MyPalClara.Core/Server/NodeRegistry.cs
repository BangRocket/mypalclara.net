using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace MyPalClara.Core.Server;

/// <summary>
/// Manages connected adapter nodes. Thread-safe via ConcurrentDictionary.
/// </summary>
public class NodeRegistry
{
    private readonly ConcurrentDictionary<WebSocket, NodeInfo> _nodesBySocket = new();
    private readonly ConcurrentDictionary<string, NodeInfo> _nodesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, NodeInfo> _nodesBySession = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a new adapter node. If the nodeId already exists, the old connection is replaced (reconnection).
    /// </summary>
    /// <returns>Tuple of (sessionId, wasReconnection).</returns>
    public (string SessionId, bool IsReconnection) Register(
        WebSocket ws,
        string nodeId,
        string platform,
        List<string>? capabilities,
        Dictionary<string, object>? metadata)
    {
        var isReconnection = false;

        // If nodeId already exists, unregister the old connection
        if (_nodesById.TryGetValue(nodeId, out var existingNode))
        {
            Unregister(existingNode.WebSocket);
            isReconnection = true;
        }

        var sessionId = Guid.NewGuid().ToString();
        var node = new NodeInfo
        {
            NodeId = nodeId,
            Platform = platform,
            WebSocket = ws,
            SessionId = sessionId,
            Capabilities = capabilities ?? [],
            Metadata = metadata ?? [],
            ConnectedAt = DateTime.UtcNow,
            LastPingAt = DateTime.UtcNow,
        };

        _nodesBySocket[ws] = node;
        _nodesById[nodeId] = node;
        _nodesBySession[sessionId] = node;

        return (sessionId, isReconnection);
    }

    /// <summary>Get node info by WebSocket instance.</summary>
    public NodeInfo? GetByWebSocket(WebSocket ws)
    {
        _nodesBySocket.TryGetValue(ws, out var node);
        return node;
    }

    /// <summary>Get node info by node ID.</summary>
    public NodeInfo? GetById(string nodeId)
    {
        _nodesById.TryGetValue(nodeId, out var node);
        return node;
    }

    /// <summary>Get node info by session ID.</summary>
    public NodeInfo? GetBySession(string sessionId)
    {
        _nodesBySession.TryGetValue(sessionId, out var node);
        return node;
    }

    /// <summary>Get all nodes for a specific platform.</summary>
    public IReadOnlyList<NodeInfo> GetByPlatform(string platform)
    {
        return _nodesById.Values
            .Where(n => n.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>Unregister a node by its WebSocket connection.</summary>
    public void Unregister(WebSocket ws)
    {
        if (_nodesBySocket.TryRemove(ws, out var node))
        {
            _nodesById.TryRemove(node.NodeId, out _);
            _nodesBySession.TryRemove(node.SessionId, out _);
        }
    }

    /// <summary>Number of connected nodes.</summary>
    public int Count => _nodesBySocket.Count;

    /// <summary>All currently connected nodes.</summary>
    public IReadOnlyCollection<NodeInfo> All => _nodesBySocket.Values.ToList().AsReadOnly();
}
