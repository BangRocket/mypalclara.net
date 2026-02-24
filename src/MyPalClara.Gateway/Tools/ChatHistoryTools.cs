using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Data;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class ChatHistoryTools
{
    public static void Register(IToolRegistry registry, IServiceScopeFactory scopeFactory)
    {
        registry.RegisterTool("search_chat_history", new ToolSchema("search_chat_history",
            "Search chat history by keyword. Args: query (string), limit (int, optional, default 20), user_id (string, optional).",
            JsonDocument.Parse("""
            {"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer"},"user_id":{"type":"string"}},"required":["query"]}
            """).RootElement),
            (args, ctx, ct) => SearchAsync(args, ctx, scopeFactory, ct));

        registry.RegisterTool("get_chat_history", new ToolSchema("get_chat_history",
            "Get recent chat messages. Args: channel_id (string, optional), limit (int, optional, default 20), user_id (string, optional).",
            JsonDocument.Parse("""
            {"type":"object","properties":{"channel_id":{"type":"string"},"limit":{"type":"integer"},"user_id":{"type":"string"}}}
            """).RootElement),
            (args, ctx, ct) => GetHistoryAsync(args, ctx, scopeFactory, ct));
    }

    public static async Task<ToolResult> SearchAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        if (!args.TryGetValue("query", out var queryElem))
            return new ToolResult(false, "", "Missing: query");

        var query = queryElem.GetString() ?? "";
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 20;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var messages = await db.Messages
            .Where(m => EF.Functions.Like(m.Content, $"%{query}%"))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .Select(m => new { m.Role, m.Content, m.CreatedAt, m.UserId })
            .ToListAsync(ct);

        if (messages.Count == 0)
            return new ToolResult(true, "No messages found.");

        var results = messages.Select(m =>
            $"[{m.CreatedAt:yyyy-MM-dd HH:mm}] {m.Role} ({m.UserId}): {m.Content[..Math.Min(200, m.Content.Length)]}");
        return new ToolResult(true, string.Join("\n", results));
    }

    public static async Task<ToolResult> GetHistoryAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 20;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var q = db.Messages.AsQueryable();

        if (args.TryGetValue("user_id", out var uidElem))
            q = q.Where(m => m.UserId == uidElem.GetString());

        var messages = await q
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content, m.CreatedAt, m.UserId })
            .ToListAsync(ct);

        if (messages.Count == 0)
            return new ToolResult(true, "No messages found.");

        var results = messages.Select(m =>
            $"[{m.CreatedAt:yyyy-MM-dd HH:mm}] {m.Role} ({m.UserId}): {m.Content[..Math.Min(200, m.Content.Length)]}");
        return new ToolResult(true, string.Join("\n", results));
    }
}
