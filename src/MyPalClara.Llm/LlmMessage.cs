namespace MyPalClara.Llm;

public abstract record LlmMessage(string Role);

public record SystemMessage(string Content) : LlmMessage("system");

public record UserMessage(string Content, IReadOnlyList<ContentPart>? Parts = null) : LlmMessage("user");

public record AssistantMessage(string? Content = null, IReadOnlyList<ToolCall>? ToolCalls = null) : LlmMessage("assistant");

public record ToolResultMessage(string ToolCallId, string Content) : LlmMessage("tool");

public record ContentPart
{
    public ContentPartType Type { get; init; }
    public string? Text { get; init; }
    public string? MediaType { get; init; }
    public string? Base64Data { get; init; }
    public string? Url { get; init; }
}

public enum ContentPartType
{
    Text,
    ImageBase64,
    ImageUrl
}
