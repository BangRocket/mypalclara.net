using System.Collections.Concurrent;

namespace MyPalClara.Gateway.Sessions;

/// <summary>
/// Represents a connected adapter (CLI, Discord, SSH, etc.).
/// Holds the WebSocket connection and per-channel session state.
/// </summary>
public sealed class AdapterSession
{
    public string ConnectionId { get; }
    public string AdapterType { get; }
    public string AdapterId { get; }
    public System.Net.WebSockets.WebSocket WebSocket { get; }
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;

    /// <summary>Per-channel session state keyed by channelId.</summary>
    public ConcurrentDictionary<string, ChannelSession> Channels { get; } = new();

    public AdapterSession(string connectionId, string adapterType, string adapterId, System.Net.WebSockets.WebSocket webSocket)
    {
        ConnectionId = connectionId;
        AdapterType = adapterType;
        AdapterId = adapterId;
        WebSocket = webSocket;
    }

    /// <summary>Get or create a channel session for the given channel ID.</summary>
    public ChannelSession GetOrCreateChannel(string channelId, string channelName, string channelType)
    {
        return Channels.GetOrAdd(channelId, _ => new ChannelSession(channelId, channelName, channelType));
    }
}
