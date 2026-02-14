namespace MyPalClara.Core.Adapters;

/// <summary>
/// Common interface for all channel adapters (CLI, Discord, Telegram, Slack, SSH).
/// Adapters are thin WS clients that connect to the Gateway.
/// </summary>
public interface IChannelAdapter : IAsyncDisposable
{
    string AdapterType { get; }
    string AdapterId { get; }
    bool IsConnected { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    AdapterStatus GetStatus();
}

public sealed record AdapterStatus(
    string AdapterType,
    string AdapterId,
    bool IsConnected,
    DateTime? ConnectedSince,
    int ActiveChannels);
