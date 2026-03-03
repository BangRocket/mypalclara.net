using Clara.Core.Llm;
using Clara.Core.Sessions;

namespace Clara.Gateway.Pipeline;

public interface IMessagePipeline
{
    Task ProcessAsync(PipelineContext context, CancellationToken ct = default);
}

public class PipelineContext
{
    public string SessionKey { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Content { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public bool Cancelled { get; set; }
    public string? ResponseText { get; set; }

    // Set by ContextBuildStage
    public Session? Session { get; set; }
    public string? SystemPrompt { get; set; }

    // Set by ToolSelectionStage
    public IReadOnlyList<ToolDefinition>? SelectedTools { get; set; }
}
