namespace MyPalClara.Core.Memory;

/// <summary>A memory record returned from vector search, possibly enriched with FSRS data.</summary>
public sealed class MemoryItem
{
    public required string Id { get; init; }
    public required string Memory { get; init; }
    public double Score { get; set; }
    public Dictionary<string, object?> Metadata { get; init; } = [];

    // FSRS enrichment (set by CompositeScorer)
    public double CompositeScore { get; set; }
    public string? Category { get; set; }
    public bool IsKey { get; set; }
}
