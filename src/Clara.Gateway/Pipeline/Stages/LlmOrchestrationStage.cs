using System.Text;
using Clara.Core.Config;
using Clara.Core.Events;
using Clara.Core.Llm;
using Clara.Core.Tools;
using Clara.Gateway.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clara.Gateway.Pipeline.Stages;

public class LlmOrchestrationStage
{
    private readonly LlmOrchestrator _orchestrator;
    private readonly ILlmProviderFactory _providerFactory;
    private readonly IClaraEventBus _eventBus;
    private readonly IHubContext<AdapterHub, IAdapterClient> _hubContext;
    private readonly GatewayOptions _options;
    private readonly ILogger<LlmOrchestrationStage> _logger;

    public LlmOrchestrationStage(
        LlmOrchestrator orchestrator,
        ILlmProviderFactory providerFactory,
        IClaraEventBus eventBus,
        IHubContext<AdapterHub, IAdapterClient> hubContext,
        IOptions<GatewayOptions> options,
        ILogger<LlmOrchestrationStage> logger)
    {
        _orchestrator = orchestrator;
        _providerFactory = providerFactory;
        _eventBus = eventBus;
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;
    }

    public virtual async Task ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        if (context.Session is null || context.SystemPrompt is null)
        {
            _logger.LogWarning("Missing session or system prompt for {SessionKey}", context.SessionKey);
            return;
        }

        // Classify tier and resolve provider/model
        var tier = TierClassifier.Classify(context.Content);
        var provider = _providerFactory.GetProvider();
        var model = _providerFactory.ResolveModel(provider.Name, tier);

        _logger.LogDebug("Using {Provider}/{Model} (tier: {Tier}) for {SessionKey}",
            provider.Name, model, tier, context.SessionKey);

        // Build messages list: system + history + new user message
        var messages = new List<LlmMessage>
        {
            LlmMessage.System(context.SystemPrompt),
        };
        messages.AddRange(context.Session.Messages);
        messages.Add(LlmMessage.User(context.Content));

        var request = new LlmRequest(
            Model: model,
            Messages: messages,
            Tools: context.SelectedTools);

        var toolContext = new ToolExecutionContext(
            UserId: context.UserId,
            SessionKey: context.SessionKey,
            Platform: context.Platform,
            IsSandboxed: false,
            WorkspaceDir: null);

        // Run orchestrator and stream events
        var responseText = new StringBuilder();

        await foreach (var evt in _orchestrator.RunAsync(provider, request, toolContext, ct))
        {
            switch (evt)
            {
                case TextDelta delta:
                    responseText.Append(delta.Text);
                    await _hubContext.Clients.Group(context.SessionKey)
                        .ReceiveTextDelta(context.SessionKey, delta.Text);
                    break;

                case ToolStarted started:
                    await _hubContext.Clients.Group(context.SessionKey)
                        .ReceiveToolStatus(context.SessionKey, started.ToolName, "started");
                    await _eventBus.PublishAsync(new ClaraEvent(ToolEvents.Start, DateTime.UtcNow,
                        new Dictionary<string, object>
                        {
                            ["tool"] = started.ToolName,
                            ["arguments"] = started.Arguments,
                        })
                    {
                        SessionKey = context.SessionKey,
                        UserId = context.UserId,
                    });
                    break;

                case ToolCompleted completed:
                    await _hubContext.Clients.Group(context.SessionKey)
                        .ReceiveToolStatus(context.SessionKey, completed.ToolName,
                            completed.Result.Success ? "completed" : "failed");
                    await _eventBus.PublishAsync(new ClaraEvent(ToolEvents.End, DateTime.UtcNow,
                        new Dictionary<string, object>
                        {
                            ["tool"] = completed.ToolName,
                            ["success"] = completed.Result.Success,
                        })
                    {
                        SessionKey = context.SessionKey,
                        UserId = context.UserId,
                    });
                    break;

                case LoopDetected loop:
                    _logger.LogWarning("Tool loop detected: {ToolName} at round {Round}", loop.ToolName, loop.Round);
                    break;

                case MaxRoundsReached max:
                    _logger.LogWarning("Max tool rounds reached ({Max}) for {SessionKey}", max.MaxRounds, context.SessionKey);
                    break;
            }
        }

        context.ResponseText = responseText.ToString();

        // Add messages to session history
        context.Session.Messages.Add(LlmMessage.User(context.Content));
        if (!string.IsNullOrEmpty(context.ResponseText))
            context.Session.Messages.Add(LlmMessage.Assistant(context.ResponseText));
        context.Session.LastActivityAt = DateTime.UtcNow;
    }
}
