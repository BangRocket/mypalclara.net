namespace MyPalClara.Core.Memory;

/// <summary>A memory record returned from vector search.</summary>
public sealed class MemoryItem
{
    public required string Id { get; init; }
    public required string Memory { get; init; }
    public double Score { get; set; }
    public Dictionary<string, object?> Metadata { get; init; } = [];

    public string? Category { get; set; }
    public bool IsKey { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
