using MyPalClara.Core.Memory;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory.Dynamics;

/// <summary>FSRS operations backed by ISemanticMemoryStore (FalkorDB).</summary>
public sealed class MemoryDynamicsService
{
    private readonly ISemanticMemoryStore _store;
    private readonly ILogger<MemoryDynamicsService> _logger;

    public MemoryDynamicsService(ISemanticMemoryStore store, ILogger<MemoryDynamicsService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Get existing FSRS state or return defaults.</summary>
    public async Task<FsrsState> GetOrCreateAsync(string memoryId, string userId)
    {
        var state = await _store.GetFsrsStateAsync(memoryId, [userId]);
        if (state is not null) return state;

        // Memory node should already exist (created by InsertMemoryAsync).
        // Return a default state — the node's FSRS fields were set on creation.
        return new FsrsState
        {
            MemoryId = memoryId,
            UserId = userId,
        };
    }

    /// <summary>Batch-get FSRS data for multiple memory IDs across linked user IDs.</summary>
    public async Task<Dictionary<string, FsrsState>> BatchGetAsync(
        IEnumerable<string> memoryIds, IReadOnlyList<string> userIds)
    {
        return await _store.BatchGetFsrsStatesAsync(memoryIds, userIds);
    }

    /// <summary>
    /// Promote a memory: run FSRS review cycle, update state, log access.
    /// </summary>
    public async Task PromoteAsync(string memoryId, IReadOnlyList<string> userIds, Grade grade, string signalType)
    {
        var primaryUserId = userIds[0];

        var fsrs = await _store.GetFsrsStateAsync(memoryId, userIds);
        if (fsrs is null)
        {
            fsrs = new FsrsState { MemoryId = memoryId, UserId = primaryUserId };
        }

        var now = DateTime.UtcNow;

        var state = new MemoryState(
            Stability: fsrs.Stability,
            Difficulty: fsrs.Difficulty,
            RetrievalStrength: fsrs.RetrievalStrength,
            StorageStrength: fsrs.StorageStrength,
            LastReview: fsrs.LastAccessedAt,
            ReviewCount: fsrs.AccessCount);

        var elapsedDays = fsrs.LastAccessedAt.HasValue
            ? (now - fsrs.LastAccessedAt.Value).TotalDays
            : 0.0;
        var currentR = fsrs.AccessCount > 0
            ? Fsrs.Retrievability(elapsedDays, fsrs.Stability)
            : 1.0;

        var result = Fsrs.Review(state, grade, now);

        // Update FSRS state on node
        fsrs.Stability = result.NewState.Stability;
        fsrs.Difficulty = result.NewState.Difficulty;
        fsrs.RetrievalStrength = result.NewState.RetrievalStrength;
        fsrs.StorageStrength = result.NewState.StorageStrength;
        fsrs.LastAccessedAt = now;
        fsrs.AccessCount++;
        fsrs.UpdatedAt = now;

        await _store.UpdateFsrsStateAsync(fsrs);

        // Record access event
        await _store.RecordAccessEventAsync(
            memoryId, primaryUserId, (int)grade, signalType, currentR);

        _logger.LogDebug("Promoted {MemoryId}: S={Stability:F2} D={Difficulty:F2} R_r={Rr:F2} R_s={Rs:F2}",
            memoryId, fsrs.Stability, fsrs.Difficulty, fsrs.RetrievalStrength, fsrs.StorageStrength);
    }

    /// <summary>Demote a memory (grade = Again).</summary>
    public Task DemoteAsync(string memoryId, IReadOnlyList<string> userIds)
        => PromoteAsync(memoryId, userIds, Grade.Again, "contradiction_detected");

    /// <summary>Record a memory supersession.</summary>
    public async Task RecordSupersessionAsync(
        string oldId, string newId, string userId, string reason, double confidence, string? details = null)
    {
        await _store.RecordSupersessionAsync(oldId, newId, userId, reason, confidence, details);
    }
}
