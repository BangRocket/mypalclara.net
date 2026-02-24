using System.Text.Json;
using MyPalClara.Llm;

namespace MyPalClara.Modules.Sdk;

public interface IToolSource
{
    string Name { get; }
    IReadOnlyList<ToolSchema> GetTools();
    bool CanHandle(string toolName);
    Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args, ToolCallContext context, CancellationToken ct = default);
}
