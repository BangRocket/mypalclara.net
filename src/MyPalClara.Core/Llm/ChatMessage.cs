namespace MyPalClara.Core.Llm;

/// <summary>Base class for all chat messages.</summary>
public abstract record ChatMessage(string Role, string? Content);

public sealed record SystemMessage(string? Content) : ChatMessage("system", Content);

public sealed record UserMessage(string? Content) : ChatMessage("user", Content);

public sealed record AssistantMessage(string? Content, IReadOnlyList<ToolCall>? ToolCalls = null)
    : ChatMessage("assistant", Content);

/// <summary>Result from a tool execution, sent back to the LLM.</summary>
public sealed record ToolResultMessage(string ToolCallId, string? Content)
    : ChatMessage("tool", Content);
