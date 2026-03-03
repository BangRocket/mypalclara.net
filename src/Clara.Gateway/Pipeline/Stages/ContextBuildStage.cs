using Clara.Core.Prompt;
using Clara.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Pipeline.Stages;

public class ContextBuildStage
{
    private readonly ISessionManager _sessionManager;
    private readonly PromptComposer _promptComposer;
    private readonly ILogger<ContextBuildStage> _logger;

    public ContextBuildStage(
        ISessionManager sessionManager,
        PromptComposer promptComposer,
        ILogger<ContextBuildStage> logger)
    {
        _sessionManager = sessionManager;
        _promptComposer = promptComposer;
        _logger = logger;
    }

    public virtual async Task ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        // Get or create session
        var session = await _sessionManager.GetOrCreateAsync(context.SessionKey, context.UserId, ct);
        context.Session = session;

        _logger.LogDebug("Session loaded: {SessionKey} ({MessageCount} messages)",
            context.SessionKey, session.Messages.Count);

        // Build system prompt
        var promptContext = new PromptContext(
            context.SessionKey,
            context.UserId,
            context.Platform,
            WorkspaceDir: null);

        context.SystemPrompt = await _promptComposer.ComposeAsync(promptContext, ct);
    }
}
