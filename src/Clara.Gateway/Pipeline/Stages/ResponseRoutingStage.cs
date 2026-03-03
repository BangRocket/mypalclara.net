using Clara.Core.Events;
using Clara.Core.Sessions;
using Clara.Gateway.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Pipeline.Stages;

public class ResponseRoutingStage
{
    private readonly IHubContext<AdapterHub, IAdapterClient> _hubContext;
    private readonly ISessionManager _sessionManager;
    private readonly IClaraEventBus _eventBus;
    private readonly ILogger<ResponseRoutingStage> _logger;

    public ResponseRoutingStage(
        IHubContext<AdapterHub, IAdapterClient> hubContext,
        ISessionManager sessionManager,
        IClaraEventBus eventBus,
        ILogger<ResponseRoutingStage> logger)
    {
        _hubContext = hubContext;
        _sessionManager = sessionManager;
        _eventBus = eventBus;
        _logger = logger;
    }

    public virtual async Task ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        // Send completion signal
        await _hubContext.Clients.Group(context.SessionKey)
            .ReceiveComplete(context.SessionKey);

        // Persist session state
        if (context.Session is not null)
            await _sessionManager.UpdateAsync(context.Session, ct);

        // Emit sent event
        await _eventBus.PublishAsync(new ClaraEvent(MessageEvents.Sent, DateTime.UtcNow,
            new Dictionary<string, object>
            {
                ["responseLength"] = context.ResponseText?.Length ?? 0,
            })
        {
            SessionKey = context.SessionKey,
            UserId = context.UserId,
            Platform = context.Platform,
        });

        _logger.LogDebug("Response routed for session {SessionKey} ({Length} chars)",
            context.SessionKey, context.ResponseText?.Length ?? 0);
    }
}
