using Clara.Gateway.Pipeline.Stages;
using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Pipeline;

public class MessagePipeline : IMessagePipeline
{
    private readonly IEnumerable<IPipelineMiddleware> _middleware;
    private readonly ContextBuildStage _contextBuild;
    private readonly ToolSelectionStage _toolSelection;
    private readonly LlmOrchestrationStage _llmOrchestration;
    private readonly ResponseRoutingStage _responseRouting;
    private readonly ILogger<MessagePipeline> _logger;

    public MessagePipeline(
        IEnumerable<IPipelineMiddleware> middleware,
        ContextBuildStage contextBuild,
        ToolSelectionStage toolSelection,
        LlmOrchestrationStage llmOrchestration,
        ResponseRoutingStage responseRouting,
        ILogger<MessagePipeline> logger)
    {
        _middleware = middleware;
        _contextBuild = contextBuild;
        _toolSelection = toolSelection;
        _llmOrchestration = llmOrchestration;
        _responseRouting = responseRouting;
        _logger = logger;
    }

    public async Task ProcessAsync(PipelineContext context, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing message for session {SessionKey}", context.SessionKey);

        // Run middleware (rate limit, stop phrase, logging) in order
        foreach (var mw in _middleware.OrderBy(m => m.Order))
        {
            await mw.InvokeAsync(context, ct);
            if (context.Cancelled)
            {
                _logger.LogInformation("Pipeline cancelled by middleware for session {SessionKey}", context.SessionKey);
                return;
            }
        }

        // Run stages
        await _contextBuild.ExecuteAsync(context, ct);
        if (context.Cancelled) return;

        await _toolSelection.ExecuteAsync(context, ct);
        if (context.Cancelled) return;

        await _llmOrchestration.ExecuteAsync(context, ct);
        if (context.Cancelled) return;

        await _responseRouting.ExecuteAsync(context, ct);

        _logger.LogInformation("Pipeline complete for session {SessionKey}", context.SessionKey);
    }
}
