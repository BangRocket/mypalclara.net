using System.Text;
using System.Text.Json;
using Clara.Core.Memory;

namespace Clara.Core.Tools.BuiltIn;

public class MemorySearchTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "query": { "type": "string", "description": "Search query for memories" },
                "limit": { "type": "integer", "description": "Max results (default 10)" }
            },
            "required": ["query"]
        }
        """).RootElement;

    private readonly IMemoryStore _memoryStore;

    public MemorySearchTool(IMemoryStore memoryStore) => _memoryStore = memoryStore;

    public string Name => "memory_search";
    public string Description => "Search memories by semantic similarity";
    public ToolCategory Category => ToolCategory.Memory;
    public JsonElement ParameterSchema => Schema;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("query", out var queryEl))
            return ToolResult.Fail("Missing required parameter: query");

        var query = queryEl.GetString();
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Fail("Query cannot be empty");

        var limit = 10;
        if (arguments.TryGetProperty("limit", out var limitEl))
            limit = limitEl.GetInt32();

        var results = await _memoryStore.SearchAsync(context.UserId, query, limit, ct);

        var sb = new StringBuilder();
        foreach (var r in results)
        {
            sb.AppendLine($"[{r.Relevance:F2}] {r.Entry.Content}");
            sb.AppendLine($"  Category: {r.Entry.Category ?? "none"} | Created: {r.Entry.CreatedAt:yyyy-MM-dd}");
            sb.AppendLine();
        }

        return ToolResult.Ok(sb.Length > 0 ? sb.ToString() : "No memories found.");
    }
}

public class MemoryListTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {}
        }
        """).RootElement;

    private readonly IMemoryStore _memoryStore;

    public MemoryListTool(IMemoryStore memoryStore) => _memoryStore = memoryStore;

    public string Name => "memory_list";
    public string Description => "List all memories for the current user";
    public ToolCategory Category => ToolCategory.Memory;
    public JsonElement ParameterSchema => Schema;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        var entries = await _memoryStore.GetAllAsync(context.UserId, ct);

        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.AppendLine($"- [{e.Id:N}] {e.Content}");
            sb.AppendLine($"  Category: {e.Category ?? "none"} | Score: {e.Score:F2} | Updated: {e.UpdatedAt:yyyy-MM-dd}");
        }

        return ToolResult.Ok(sb.Length > 0 ? sb.ToString() : "No memories stored.");
    }
}

public class MemoryExportTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {}
        }
        """).RootElement;

    private readonly IMemoryView _memoryView;

    public MemoryExportTool(IMemoryView memoryView) => _memoryView = memoryView;

    public string Name => "memory_export";
    public string Description => "Export all memories as Markdown";
    public ToolCategory Category => ToolCategory.Memory;
    public JsonElement ParameterSchema => Schema;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        var markdown = await _memoryView.ExportToMarkdownAsync(context.UserId, ct);
        return ToolResult.Ok(markdown);
    }
}
