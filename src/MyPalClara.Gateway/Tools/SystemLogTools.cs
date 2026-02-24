using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Data;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class SystemLogTools
{
    public static void Register(IToolRegistry registry, IServiceScopeFactory scopeFactory)
    {
        registry.RegisterTool("search_logs", new ToolSchema("search_logs",
            "Search system logs by keyword, logger, or level. Args: keyword (string, optional), logger (string, optional), level (string, optional), limit (int, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"keyword":{"type":"string"},"logger":{"type":"string"},"level":{"type":"string"},"limit":{"type":"integer"}}}""").RootElement),
            (args, ctx, ct) => SearchLogsAsync(args, ctx, scopeFactory, ct));

        registry.RegisterTool("get_recent_logs", new ToolSchema("get_recent_logs",
            "Get recent log entries. Args: limit (int, optional, default 50).",
            JsonDocument.Parse("""{"type":"object","properties":{"limit":{"type":"integer"}}}""").RootElement),
            (args, ctx, ct) => GetRecentLogsAsync(args, ctx, scopeFactory, ct));

        registry.RegisterTool("get_error_logs", new ToolSchema("get_error_logs",
            "Get recent error log entries with tracebacks. Args: limit (int, optional, default 20).",
            JsonDocument.Parse("""{"type":"object","properties":{"limit":{"type":"integer"}}}""").RootElement),
            (args, ctx, ct) => GetErrorLogsAsync(args, ctx, scopeFactory, ct));
    }

    public static async Task<ToolResult> SearchLogsAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 50;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var q = db.LogEntries.AsQueryable();

        if (args.TryGetValue("keyword", out var kw))
            q = q.Where(l => EF.Functions.Like(l.Message, $"%{kw.GetString()}%"));
        if (args.TryGetValue("logger", out var lg))
            q = q.Where(l => l.LoggerName == lg.GetString());
        if (args.TryGetValue("level", out var lv))
            q = q.Where(l => l.Level == lv.GetString());

        var logs = await q.OrderByDescending(l => l.Timestamp).Take(limit)
            .Select(l => new { l.Timestamp, l.Level, l.LoggerName, l.Message }).ToListAsync(ct);

        if (logs.Count == 0) return new ToolResult(true, "No logs found.");
        var results = logs.Select(l => $"[{l.Timestamp:yyyy-MM-dd HH:mm:ss}] [{l.Level}] {l.LoggerName}: {l.Message}");
        return new ToolResult(true, string.Join("\n", results));
    }

    public static async Task<ToolResult> GetRecentLogsAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 50;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var logs = await db.LogEntries.OrderByDescending(l => l.Timestamp).Take(limit)
            .Select(l => new { l.Timestamp, l.Level, l.LoggerName, l.Message }).ToListAsync(ct);

        if (logs.Count == 0) return new ToolResult(true, "No logs found.");
        var results = logs.Select(l => $"[{l.Timestamp:yyyy-MM-dd HH:mm:ss}] [{l.Level}] {l.LoggerName}: {l.Message}");
        return new ToolResult(true, string.Join("\n", results));
    }

    public static async Task<ToolResult> GetErrorLogsAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 20;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var logs = await db.LogEntries
            .Where(l => l.Level == "ERROR" || l.Level == "Error" || l.Level == "CRITICAL")
            .OrderByDescending(l => l.Timestamp).Take(limit)
            .Select(l => new { l.Timestamp, l.Level, l.LoggerName, l.Message, l.Exception }).ToListAsync(ct);

        if (logs.Count == 0) return new ToolResult(true, "No error logs found.");
        var results = logs.Select(l =>
        {
            var entry = $"[{l.Timestamp:yyyy-MM-dd HH:mm:ss}] [{l.Level}] {l.LoggerName}: {l.Message}";
            if (!string.IsNullOrEmpty(l.Exception)) entry += $"\n  {l.Exception}";
            return entry;
        });
        return new ToolResult(true, string.Join("\n", results));
    }
}
