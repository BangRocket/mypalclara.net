using MyPalClara.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.Scheduling;

/// <summary>Verifies database connectivity and logs basic metrics.</summary>
public sealed class HealthCheckJob : IScheduledJob
{
    private readonly IDbContextFactory<ClaraDbContext> _dbFactory;
    private readonly ILogger<HealthCheckJob> _logger;

    public string Name => "health-check";

    public HealthCheckJob(IDbContextFactory<ClaraDbContext> dbFactory, ILogger<HealthCheckJob> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var canConnect = await db.Database.CanConnectAsync(ct);

        if (!canConnect)
        {
            _logger.LogError("Health check: database connection failed");
            return;
        }

        var userCount = await db.Users.CountAsync(ct);
        var conversationCount = await db.Conversations.CountAsync(ct);
        var messageCount = await db.Messages.CountAsync(ct);

        _logger.LogInformation(
            "Health check OK — {Users} users, {Conversations} conversations, {Messages} messages",
            userCount, conversationCount, messageCount);
    }
}
