using System.Collections.Concurrent;
using MyPalClara.Gateway.Sessions;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.WebSocket;

/// <summary>Manages active adapter connections.</summary>
public sealed class ConnectionManager
{
    private readonly ConcurrentDictionary<string, AdapterSession> _sessions = new();
    private readonly ILogger<ConnectionManager> _logger;

    public ConnectionManager(ILogger<ConnectionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>Register a new adapter session after authentication.</summary>
    public void Add(AdapterSession session)
    {
        _sessions[session.ConnectionId] = session;
        _logger.LogInformation("Adapter connected: {Type}/{Id} (conn={ConnId})",
            session.AdapterType, session.AdapterId, session.ConnectionId);
    }

    /// <summary>Remove a disconnected adapter session.</summary>
    public void Remove(string connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            _logger.LogInformation("Adapter disconnected: {Type}/{Id} (conn={ConnId})",
                session.AdapterType, session.AdapterId, connectionId);
        }
    }

    /// <summary>Get all active sessions.</summary>
    public IReadOnlyCollection<AdapterSession> GetAll() => _sessions.Values.ToList();

    /// <summary>Get a session by connection ID.</summary>
    public AdapterSession? Get(string connectionId) =>
        _sessions.TryGetValue(connectionId, out var session) ? session : null;

    /// <summary>Total active connections.</summary>
    public int Count => _sessions.Count;
}
