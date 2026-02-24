namespace MyPalClara.Llm;

public record LlmResponse(
    string? Content,
    IReadOnlyList<ToolCall> ToolCalls,
    string? StopReason)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
}
