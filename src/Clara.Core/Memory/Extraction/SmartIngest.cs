using Clara.Core.Memory.Dynamics;
using Clara.Core.Memory.Vector;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Memory.Extraction;

/// <summary>
/// Smart memory ingestion pipeline. Port of memory_manager.py:smart_ingest().
/// Handles dedup, contradiction detection, and supersession.
/// </summary>
public sealed class SmartIngest
{
    private readonly IVectorStore _vectorStore;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ContradictionDetector _contradictionDetector;
    private readonly MemoryDynamicsService _dynamics;
    private readonly ILogger<SmartIngest> _logger;

    public SmartIngest(
        IVectorStore vectorStore,
        EmbeddingClient embeddingClient,
        ContradictionDetector contradictionDetector,
        MemoryDynamicsService dynamics,
        ILogger<SmartIngest> logger)
    {
        _vectorStore = vectorStore;
        _embeddingClient = embeddingClient;
        _contradictionDetector = contradictionDetector;
        _dynamics = dynamics;
        _logger = logger;
    }

    /// <summary>
    /// Ingest a new fact. Checks for duplicates, contradictions, and supersessions.
    /// Returns the action taken.
    /// </summary>
    public async Task<IngestResult> IngestAsync(
        string fact, string userId, CancellationToken ct = default)
    {
        // Generate embedding
        var embedding = await _embeddingClient.EmbedAsync(fact, ct);

        // Search for similar existing memories
        var similar = await _vectorStore.SearchAsync(
            embedding,
            new Dictionary<string, object?> { ["user_id"] = userId },
            limit: 5, ct: ct);

        if (similar.Count == 0)
        {
            // No similar memories — create new
            return await CreateNewMemoryAsync(fact, userId, embedding, ct);
        }

        var best = similar[0];
        var textSim = ContradictionDetector.CalculateSimilarity(fact, best.Memory);

        // Decision thresholds (from memory_manager.py:smart_ingest lines 1972-2081)
        if (best.Score > 0.95 || textSim > 0.9)
        {
            // Near-duplicate — skip
            _logger.LogDebug("Skipping near-duplicate: score={Score:F3} textSim={TextSim:F3}", best.Score, textSim);
            return new IngestResult(IngestAction.Skip, "Near-duplicate detected");
        }

        if (best.Score > 0.75)
        {
            // High similarity — check contradiction
            var contradiction = await _contradictionDetector.DetectAsync(fact, best.Memory, ct: ct);
            if (contradiction.Contradicts)
            {
                return await SupersedeAsync(fact, userId, best, embedding, contradiction, ct);
            }

            // Similar but not contradictory — update (treat as reinforcement)
            _logger.LogDebug("Similar memory reinforced: {Id}", best.Id);
            await _dynamics.PromoteAsync(best.Id, [userId], Grade.Good, "implicit_reference");
            return new IngestResult(IngestAction.Reinforced, "Existing memory reinforced");
        }

        if (best.Score > 0.6)
        {
            // Moderate similarity — check contradiction with lower threshold
            var contradiction = await _contradictionDetector.DetectAsync(fact, best.Memory, ct: ct);
            if (contradiction.Contradicts && contradiction.Confidence > 0.7)
            {
                return await SupersedeAsync(fact, userId, best, embedding, contradiction, ct);
            }
        }

        // Low similarity — create new memory
        return await CreateNewMemoryAsync(fact, userId, embedding, ct);
    }

    private async Task<IngestResult> CreateNewMemoryAsync(
        string fact, string userId, float[] embedding, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString();
        var category = CategoryClassifier.Classify(fact);

        var payload = new Dictionary<string, object?>
        {
            ["data"] = fact,
            ["user_id"] = userId,
            ["created_at"] = DateTime.UtcNow.ToString("o"),
        };

        if (category is not null)
            payload["category"] = category;

        await _vectorStore.InsertAsync(id, embedding, payload, ct);

        // Create FSRS dynamics record
        await _dynamics.GetOrCreateAsync(id, userId);

        _logger.LogDebug("Created new memory: {Id} category={Category}", id, category);
        return new IngestResult(IngestAction.Created, "New memory created", id);
    }

    private async Task<IngestResult> SupersedeAsync(
        string fact, string userId, MemoryItem oldMemory, float[] embedding,
        ContradictionResult contradiction, CancellationToken ct)
    {
        // Create new memory
        var result = await CreateNewMemoryAsync(fact, userId, embedding, ct);

        // Demote old memory
        await _dynamics.DemoteAsync(oldMemory.Id, [userId]);

        // Record supersession
        if (result.MemoryId is not null)
        {
            await _dynamics.RecordSupersessionAsync(
                oldMemory.Id, result.MemoryId, userId,
                "contradiction", contradiction.Confidence,
                contradiction.Explanation);
        }

        _logger.LogInformation("Superseded memory {OldId} with {NewId}: {Reason}",
            oldMemory.Id, result.MemoryId, contradiction.Explanation);

        return new IngestResult(IngestAction.Superseded, contradiction.Explanation, result.MemoryId);
    }
}

public enum IngestAction { Created, Skip, Reinforced, Superseded }

public sealed record IngestResult(IngestAction Action, string Reason, string? MemoryId = null);
