using MyPalClara.Core.Memory;
using MyPalClara.Memory.Dynamics;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory.Extraction;

/// <summary>
/// Smart memory ingestion pipeline.
/// Handles dedup, contradiction detection, and supersession.
/// </summary>
public sealed class SmartIngest
{
    private readonly ISemanticMemoryStore _store;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ContradictionDetector _contradictionDetector;
    private readonly MemoryDynamicsService _dynamics;
    private readonly ILogger<SmartIngest> _logger;

    public SmartIngest(
        ISemanticMemoryStore store,
        EmbeddingClient embeddingClient,
        ContradictionDetector contradictionDetector,
        MemoryDynamicsService dynamics,
        ILogger<SmartIngest> logger)
    {
        _store = store;
        _embeddingClient = embeddingClient;
        _contradictionDetector = contradictionDetector;
        _dynamics = dynamics;
        _logger = logger;
    }

    /// <summary>
    /// Ingest a new fact. Checks for duplicates, contradictions, and supersessions.
    /// </summary>
    public async Task<IngestResult> IngestAsync(
        string fact, string userId, CancellationToken ct = default)
    {
        var embedding = await _embeddingClient.EmbedAsync(fact, ct);

        var similar = await _store.SearchAsync(embedding, [userId], limit: 5, ct: ct);

        if (similar.Count == 0)
        {
            return await CreateNewMemoryAsync(fact, userId, embedding, ct);
        }

        var best = similar[0];
        var textSim = ContradictionDetector.CalculateSimilarity(fact, best.Memory);

        if (best.Score > 0.95 || textSim > 0.9)
        {
            _logger.LogDebug("Skipping near-duplicate: score={Score:F3} textSim={TextSim:F3}", best.Score, textSim);
            return new IngestResult(IngestAction.Skip, "Near-duplicate detected");
        }

        if (best.Score > 0.75)
        {
            var contradiction = await _contradictionDetector.DetectAsync(fact, best.Memory, ct: ct);
            if (contradiction.Contradicts)
            {
                return await SupersedeAsync(fact, userId, best, embedding, contradiction, ct);
            }

            _logger.LogDebug("Similar memory reinforced: {Id}", best.Id);
            await _dynamics.PromoteAsync(best.Id, [userId], Grade.Good, "implicit_reference");
            return new IngestResult(IngestAction.Reinforced, "Existing memory reinforced");
        }

        if (best.Score > 0.6)
        {
            var contradiction = await _contradictionDetector.DetectAsync(fact, best.Memory, ct: ct);
            if (contradiction.Contradicts && contradiction.Confidence > 0.7)
            {
                return await SupersedeAsync(fact, userId, best, embedding, contradiction, ct);
            }
        }

        return await CreateNewMemoryAsync(fact, userId, embedding, ct);
    }

    private async Task<IngestResult> CreateNewMemoryAsync(
        string fact, string userId, float[] embedding, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString();
        var category = CategoryClassifier.Classify(fact);

        var metadata = new Dictionary<string, object?>
        {
            ["created_at"] = DateTime.UtcNow.ToString("o"),
        };
        if (category is not null)
            metadata["category"] = category;

        await _store.InsertMemoryAsync(id, embedding, fact, userId, metadata, ct);

        _logger.LogDebug("Created new memory: {Id} category={Category}", id, category);
        return new IngestResult(IngestAction.Created, "New memory created", id);
    }

    private async Task<IngestResult> SupersedeAsync(
        string fact, string userId, MemoryItem oldMemory, float[] embedding,
        ContradictionResult contradiction, CancellationToken ct)
    {
        var result = await CreateNewMemoryAsync(fact, userId, embedding, ct);

        await _dynamics.DemoteAsync(oldMemory.Id, [userId]);

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
