namespace Clara.Core.Llm;

public record LlmStreamChunk
{
    public string? TextDelta { get; init; }
    public ToolCallContent? ToolCall { get; init; }
    public bool IsTextDelta => TextDelta is not null;
    public bool IsToolCall => ToolCall is not null;
}
