using Clara.Core.Config;
using Clara.Core.Events;
using Clara.Gateway.Queues;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clara.Gateway.Hubs;

public class AdapterHub : Hub<IAdapterClient>
{
    private readonly LaneQueueManager _queueManager;
    private readonly QueueMetrics _metrics;
    private readonly IClaraEventBus _eventBus;
    private readonly GatewayOptions _options;
    private readonly ILogger<AdapterHub> _logger;

    public AdapterHub(
        LaneQueueManager queueManager,
        QueueMetrics metrics,
        IClaraEventBus eventBus,
        IOptions<GatewayOptions> options,
        ILogger<AdapterHub> logger)
    {
        _queueManager = queueManager;
        _metrics = metrics;
        _eventBus = eventBus;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Adapter sends a message to the gateway for processing.
    /// </summary>
    public async Task SendMessage(string sessionKey, string userId, string platform, string content)
    {
        _logger.LogInformation("Message received from {Platform}/{UserId} for session {SessionKey}",
            platform, userId, sessionKey);

        var message = new SessionMessage(
            SessionKey: sessionKey,
            UserId: userId,
            Platform: platform,
            Content: content,
            ConnectionId: Context.ConnectionId,
            ReceivedAt: DateTime.UtcNow);

        await _queueManager.EnqueueAsync(sessionKey, message);
        _metrics.RecordEnqueue();

        await _eventBus.PublishAsync(new ClaraEvent(MessageEvents.Received, DateTime.UtcNow)
        {
            UserId = userId,
            SessionKey = sessionKey,
            Platform = platform,
        });
    }

    /// <summary>
    /// Adapter authenticates with the gateway.
    /// </summary>
    public Task Authenticate(string secret)
    {
        if (_options.Secret is not null && secret != _options.Secret)
        {
            _logger.LogWarning("Authentication failed for connection {ConnectionId}", Context.ConnectionId);
            Context.Abort();
            return Task.CompletedTask;
        }

        Context.Items["authenticated"] = true;
        _logger.LogInformation("Connection {ConnectionId} authenticated", Context.ConnectionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Subscribe to responses for a specific session.
    /// </summary>
    public async Task Subscribe(string sessionKey)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionKey);
        _logger.LogDebug("Connection {ConnectionId} subscribed to {SessionKey}", Context.ConnectionId, sessionKey);
    }

    /// <summary>
    /// Unsubscribe from responses for a specific session.
    /// </summary>
    public async Task Unsubscribe(string sessionKey)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionKey);
        _logger.LogDebug("Connection {ConnectionId} unsubscribed from {SessionKey}", Context.ConnectionId, sessionKey);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Adapter connected: {ConnectionId}", Context.ConnectionId);
        await _eventBus.PublishAsync(new ClaraEvent(AdapterEvents.Connected, DateTime.UtcNow,
            new Dictionary<string, object> { ["connectionId"] = Context.ConnectionId }));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Adapter disconnected: {ConnectionId} ({Reason})",
            Context.ConnectionId, exception?.Message ?? "clean");
        await _eventBus.PublishAsync(new ClaraEvent(AdapterEvents.Disconnected, DateTime.UtcNow,
            new Dictionary<string, object> { ["connectionId"] = Context.ConnectionId }));
        await base.OnDisconnectedAsync(exception);
    }
}
