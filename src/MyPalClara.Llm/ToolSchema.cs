using System.Text.Json;

namespace MyPalClara.Llm;

public record ToolSchema(string Name, string Description, JsonElement Parameters);
