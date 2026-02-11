using Clara.Core.Data;
using Clara.Core.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Memory.Dynamics;

/// <summary>CRUD operations for FSRS data tables. Wraps EF Core.</summary>
public sealed class MemoryDynamicsService
{
    private readonly IDbContextFactory<ClaraDbContext> _dbFactory;
    private readonly ILogger<MemoryDynamicsService> _logger;

    public MemoryDynamicsService(IDbContextFactory<ClaraDbContext> dbFactory, ILogger<MemoryDynamicsService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>Get existing or create with defaults.</summary>
    public async Task<MemoryDynamicsEntity> GetOrCreateAsync(string memoryId, string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var entity = await db.MemoryDynamics
            .FirstOrDefaultAsync(e => e.MemoryId == memoryId && e.UserId == userId);

        if (entity is not null) return entity;

        entity = new MemoryDynamicsEntity
        {
            MemoryId = memoryId,
            UserId = userId,
        };
        db.MemoryDynamics.Add(entity);
        await db.SaveChangesAsync();

        _logger.LogDebug("Created MemoryDynamics for {MemoryId}", memoryId);
        return entity;
    }

    /// <summary>Batch-get FSRS data for multiple memory IDs across linked user IDs.</summary>
    public async Task<Dictionary<string, MemoryDynamicsEntity>> BatchGetAsync(
        IEnumerable<string> memoryIds, IReadOnlyList<string> userIds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var ids = memoryIds.ToHashSet();

        var entities = await db.MemoryDynamics
            .Where(e => ids.Contains(e.MemoryId) && userIds.Contains(e.UserId))
            .GroupBy(e => e.MemoryId)
            .Select(g => g.First())
            .ToDictionaryAsync(e => e.MemoryId);

        return entities;
    }

    /// <summary>
    /// Promote a memory: run FSRS review cycle, update state, log access.
    /// Searches across all linked user IDs, creates under primary (first) user ID.
    /// </summary>
    public async Task PromoteAsync(string memoryId, IReadOnlyList<string> userIds, Grade grade, string signalType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var primaryUserId = userIds[0];

        var entity = await db.MemoryDynamics
            .FirstOrDefaultAsync(e => e.MemoryId == memoryId && userIds.Contains(e.UserId));

        if (entity is null)
        {
            entity = new MemoryDynamicsEntity { MemoryId = memoryId, UserId = primaryUserId };
            db.MemoryDynamics.Add(entity);
        }

        var now = DateTime.UtcNow;

        // Build current state
        var state = new MemoryState(
            Stability: entity.Stability,
            Difficulty: entity.Difficulty,
            RetrievalStrength: entity.RetrievalStrength,
            StorageStrength: entity.StorageStrength,
            LastReview: entity.LastAccessedAt,
            ReviewCount: entity.AccessCount);

        // Calculate current retrievability for logging
        var elapsedDays = entity.LastAccessedAt.HasValue
            ? (now - entity.LastAccessedAt.Value).TotalDays
            : 0.0;
        var currentR = entity.AccessCount > 0
            ? Fsrs.Retrievability(elapsedDays, entity.Stability)
            : 1.0;

        // Run FSRS review
        var result = Fsrs.Review(state, grade, now);

        // Update entity
        entity.Stability = result.NewState.Stability;
        entity.Difficulty = result.NewState.Difficulty;
        entity.RetrievalStrength = result.NewState.RetrievalStrength;
        entity.StorageStrength = result.NewState.StorageStrength;
        entity.LastAccessedAt = now;
        entity.AccessCount++;
        entity.UpdatedAt = now;

        // Create access log
        db.MemoryAccessLog.Add(new MemoryAccessLogEntity
        {
            MemoryId = memoryId,
            UserId = primaryUserId,
            Grade = (int)grade,
            SignalType = signalType,
            RetrievabilityAtAccess = currentR,
            AccessedAt = now,
        });

        await db.SaveChangesAsync();

        _logger.LogDebug("Promoted {MemoryId}: S={Stability:F2} D={Difficulty:F2} R_r={Rr:F2} R_s={Rs:F2}",
            memoryId, entity.Stability, entity.Difficulty, entity.RetrievalStrength, entity.StorageStrength);
    }

    /// <summary>Demote a memory (grade = Again).</summary>
    public Task DemoteAsync(string memoryId, IReadOnlyList<string> userIds)
        => PromoteAsync(memoryId, userIds, Grade.Again, "contradiction_detected");

    /// <summary>Record a memory supersession.</summary>
    public async Task RecordSupersessionAsync(
        string oldId, string newId, string userId, string reason, double confidence, string? details = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        db.MemorySupersessions.Add(new MemorySupersessionEntity
        {
            OldMemoryId = oldId,
            NewMemoryId = newId,
            UserId = userId,
            Reason = reason,
            Confidence = confidence,
            Details = details,
        });

        await db.SaveChangesAsync();
    }
}
