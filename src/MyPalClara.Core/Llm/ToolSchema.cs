using System.Text.Json;

namespace MyPalClara.Core.Llm;

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

    /// <summary>Converts to OpenAI function-calling tool format.</summary>
    public Dictionary<string, object?> ToOpenAiFormat() => new()
    {
        ["type"] = "function",
        ["function"] = new Dictionary<string, object?>
        {
            ["name"] = Name,
            ["description"] = Description,
            ["parameters"] = InputSchema,
        },
    };
}
