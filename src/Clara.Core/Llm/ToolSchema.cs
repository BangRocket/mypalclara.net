using System.Text.Json;

namespace Clara.Core.Llm;

/// <summary>Schema definition for a tool available to the LLM.</summary>
public sealed record ToolSchema(string Name, string Description, JsonElement InputSchema)
{
    /// <summary>Converts to Anthropic tool format.</summary>
    public Dictionary<string, object?> ToAnthropicFormat() => new()
    {
        ["name"] = Name,
        ["description"] = Description,
        ["input_schema"] = InputSchema,
    };
}
