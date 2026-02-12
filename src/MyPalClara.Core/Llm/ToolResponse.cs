namespace MyPalClara.Core.Llm;

/// <summary>Complete response from the LLM, possibly containing tool calls.</summary>
public sealed record ToolResponse(
    string? Content,
    IReadOnlyList<ToolCall> ToolCalls,
    string? StopReason)
{
    public bool HasToolCalls => ToolCalls.Count > 0;

    /// <summary>Convert to an AssistantMessage for appending to conversation history.</summary>
    public AssistantMessage ToAssistantMessage() => new(Content, ToolCalls);
}
