using System.Text.Json;

namespace MyPalClara.Llm;

public record ToolCall(string Id, string Name, Dictionary<string, JsonElement> Arguments);
