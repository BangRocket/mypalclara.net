namespace MyPalClara.Llm;

public record ToolResponse(
    string? Content,
    IReadOnlyList<ToolCall> ToolCalls,
    string? StopReason = null)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
}
