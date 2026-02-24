using System.Text.Json;
using MyPalClara.Llm;

namespace MyPalClara.Modules.Sdk;

public interface IToolRegistry
{
    void RegisterTool(string name, ToolSchema schema, Func<ToolCallContext, Task<ToolResult>> handler);
    void RegisterSource(IToolSource source);
    void UnregisterTool(string name);
    IReadOnlyList<ToolSchema> GetAllTools(ToolFilter? filter = null);
    Task<ToolResult> ExecuteAsync(string name, Dictionary<string, JsonElement> args, ToolCallContext context, CancellationToken ct = default);
}
