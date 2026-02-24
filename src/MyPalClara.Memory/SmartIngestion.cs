using MyPalClara.Memory.FactExtraction;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory;

/// <summary>
/// Handles deduplication and smart ingestion of extracted facts into Rook memory.
/// Compares new facts against existing memories using vector similarity:
///   >= 0.95 → duplicate, skip
///   >= 0.75 → elaboration, update existing
///   >= 0.60 → contradiction/supersession, supersede old
///   &lt; 0.60  → novel, create new
/// </summary>
public class SmartIngestion
{
    private readonly IRookMemory _rook;
    private readonly ILogger<SmartIngestion> _logger;

    private const double SkipThreshold = 0.95;
    private const double UpdateThreshold = 0.75;
    private const double SupersedeThreshold = 0.60;

    public SmartIngestion(IRookMemory rook, ILogger<SmartIngestion> logger)
    {
        _rook = rook ?? throw new ArgumentNullException(nameof(rook));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IngestResult> IngestFactAsync(
        ExtractedFact fact, string userId, CancellationToken ct = default)
    {
        // 1. Search for similar existing memories
        var results = await _rook.SearchAsync(fact.Text, userId, limit: 3, ct: ct);

        if (results.Length > 0)
        {
            var topScore = results[0].Score;
            var topId = results[0].Point.Id;

            if (topScore >= SkipThreshold)
            {
                _logger.LogDebug("Skipping duplicate fact (score={Score:F3}): {Text}", topScore, fact.Text);
                return new IngestResult(IngestAction.Skipped, null, topId);
            }

            if (topScore >= UpdateThreshold)
            {
                // Update: re-embed and update the existing point with the new text
                await _rook.UpdateAsync(topId, fact.Text, userId, isKey: fact.IsKey, ct: ct);
                _logger.LogDebug("Updated existing memory {Id} (score={Score:F3}): {Text}", topId, topScore, fact.Text);
                return new IngestResult(IngestAction.Updated, topId, topId);
            }

            if (topScore >= SupersedeThreshold)
            {
                // Supersede: create new, record supersession
                var newId = await _rook.AddAsync(fact.Text, userId, isKey: fact.IsKey, ct: ct);
                _logger.LogDebug("Superseded memory {OldId} with {NewId} (score={Score:F3}): {Text}",
                    topId, newId, topScore, fact.Text);
                return new IngestResult(IngestAction.Superseded, newId, topId);
            }
        }

        // Novel: create new memory
        var id = await _rook.AddAsync(fact.Text, userId, isKey: fact.IsKey, ct: ct);
        _logger.LogDebug("Created new memory {Id}: {Text}", id, fact.Text);
        return new IngestResult(IngestAction.Created, id, null);
    }
}

public enum IngestAction { Created, Updated, Superseded, Skipped }

public record IngestResult(IngestAction Action, string? NewId, string? ExistingId);
