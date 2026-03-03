using System.Text.Json;

namespace Clara.Core.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    ToolCategory Category { get; }
    JsonElement ParameterSchema { get; }
    Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default);
}
