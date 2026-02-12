using System.Text.Json;

namespace MyPalClara.Core.Llm;

/// <summary>A tool call requested by the LLM.</summary>
public sealed record ToolCall(string Id, string Name, JsonElement Arguments);
