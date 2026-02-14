using MyPalClara.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.Scheduling;

/// <summary>Archives stale conversations that have been inactive for over 24 hours.</summary>
public sealed class ConversationArchivalJob : IScheduledJob
{
    private readonly IDbContextFactory<ClaraDbContext> _dbFactory;
    private readonly ILogger<ConversationArchivalJob> _logger;

    public string Name => "conversation-archival";

    public ConversationArchivalJob(IDbContextFactory<ClaraDbContext> dbFactory, ILogger<ConversationArchivalJob> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var archived = await db.Conversations
            .Where(c => c.LastActivityAt < cutoff && !c.Archived)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Archived, true), ct);

        _logger.LogInformation("Conversation archival: archived {Count} stale conversations", archived);
    }
}
