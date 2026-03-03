using System.Text.Json;

namespace Clara.Core.Llm;

public abstract record LlmContent;
public record TextContent(string Text) : LlmContent;
public record ImageContent(string Base64, string MediaType) : LlmContent;
public record ToolCallContent(string Id, string Name, JsonElement Arguments) : LlmContent;
public record ToolResultContent(string ToolCallId, string Content, bool IsError = false) : LlmContent;
