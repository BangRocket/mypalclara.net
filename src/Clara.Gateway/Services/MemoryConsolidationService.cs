using Clara.Core.Data;
using Clara.Core.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Services;

public class MemoryConsolidationService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IClaraEventBus _eventBus;
    private readonly ILogger<MemoryConsolidationService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);
    private readonly float _lowScoreThreshold = 0.1f;

    public MemoryConsolidationService(
        IServiceProvider services,
        IClaraEventBus eventBus,
        ILogger<MemoryConsolidationService> logger)
    {
        _services = services;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Memory consolidation service started (interval: {Interval})", _checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_checkInterval, stoppingToken);

            try
            {
                await ConsolidateAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during memory consolidation");
            }
        }
    }

    private async Task ConsolidateAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        // Prune low-score memories
        var lowScoreMemories = await db.Memories
            .Where(m => m.Score < _lowScoreThreshold)
            .ToListAsync(ct);

        if (lowScoreMemories.Count > 0)
        {
            db.Memories.RemoveRange(lowScoreMemories);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Pruned {Count} low-score memories (threshold: {Threshold})",
                lowScoreMemories.Count, _lowScoreThreshold);

            await _eventBus.PublishAsync(new ClaraEvent(MemoryEvents.Write, DateTime.UtcNow,
                new Dictionary<string, object>
                {
                    ["action"] = "consolidation",
                    ["pruned"] = lowScoreMemories.Count,
                }));
        }

        var totalMemories = await db.Memories.CountAsync(ct);
        _logger.LogDebug("Memory consolidation complete. Total memories: {Total}", totalMemories);
    }
}
