using System.Text.Json;

namespace Clara.Core.Llm;

public record ToolDefinition(string Name, string Description, JsonElement ParameterSchema);
