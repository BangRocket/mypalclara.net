using Clara.Core.Data.Models;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Memory.Dynamics;

/// <summary>
/// Re-ranks vector search results by combining semantic similarity with FSRS scores.
/// Formula: composite = 0.6 * semantic + 0.4 * fsrs
/// where fsrs = (0.7 * retrievability + 0.3 * storage_strength) * importance_weight
/// </summary>
public sealed class CompositeScorer
{
    private readonly MemoryDynamicsService _dynamicsService;
    private readonly ILogger<CompositeScorer> _logger;

    public CompositeScorer(MemoryDynamicsService dynamicsService, ILogger<CompositeScorer> logger)
    {
        _dynamicsService = dynamicsService;
        _logger = logger;
    }

    /// <summary>Re-rank a list of memory items using FSRS composite scoring (across linked user IDs).</summary>
    public async Task<List<MemoryItem>> RankAsync(List<MemoryItem> items, IReadOnlyList<string> userIds)
    {
        if (items.Count == 0) return items;

        // Batch-load FSRS data across all linked user IDs
        var memoryIds = items.Select(i => i.Id);
        var dynamics = await _dynamicsService.BatchGetAsync(memoryIds, userIds);

        var now = DateTime.UtcNow;

        foreach (var item in items)
        {
            if (dynamics.TryGetValue(item.Id, out var dyn))
            {
                var elapsedDays = dyn.LastAccessedAt.HasValue
                    ? (now - dyn.LastAccessedAt.Value).TotalDays
                    : 0.0;

                var retrievability = dyn.AccessCount > 0
                    ? Fsrs.Retrievability(elapsedDays, dyn.Stability)
                    : 1.0;

                var fsrsScore = Fsrs.CalculateMemoryScore(
                    retrievability, dyn.StorageStrength, dyn.ImportanceWeight);

                item.CompositeScore = 0.6 * item.Score + 0.4 * fsrsScore;
                item.Category = dyn.Category;
                item.IsKey = dyn.IsKey;
            }
            else
            {
                // No FSRS data — use semantic score only
                item.CompositeScore = item.Score;
            }
        }

        items.Sort((a, b) => b.CompositeScore.CompareTo(a.CompositeScore));

        _logger.LogDebug("Ranked {Count} items with FSRS composite scoring", items.Count);
        return items;
    }
}
