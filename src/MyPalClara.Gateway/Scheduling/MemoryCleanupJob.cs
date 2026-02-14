using MyPalClara.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.Scheduling;

/// <summary>Prunes old memory history entries beyond a configurable threshold.</summary>
public sealed class MemoryCleanupJob : IScheduledJob
{
    private readonly IDbContextFactory<ClaraDbContext> _dbFactory;
    private readonly ILogger<MemoryCleanupJob> _logger;

    public string Name => "memory-cleanup";

    public MemoryCleanupJob(IDbContextFactory<ClaraDbContext> dbFactory, ILogger<MemoryCleanupJob> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var deleted = await db.MemoryHistory
            .Where(m => m.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        _logger.LogInformation("Memory cleanup: pruned {Count} old history entries", deleted);
    }
}
