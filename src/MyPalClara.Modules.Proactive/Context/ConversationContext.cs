using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Data;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Modules.Proactive.Context;

public static class ConversationContext
{
    public static async Task<string> GatherAsync(string userId, IServiceScopeFactory scopeFactory,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var recentMessages = await db.Messages
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(5)
            .Select(m => new { m.Content, m.CreatedAt })
            .ToListAsync(ct);

        if (recentMessages.Count == 0)
            return "No recent conversation history.";

        var lastMsg = recentMessages.First();
        var timeSince = DateTime.UtcNow - lastMsg.CreatedAt;
        return $"Last message: {timeSince.TotalHours:F1}h ago. Recent topics: {string.Join(", ", recentMessages.Select(m => m.Content[..Math.Min(50, m.Content.Length)]))}";
    }
}
