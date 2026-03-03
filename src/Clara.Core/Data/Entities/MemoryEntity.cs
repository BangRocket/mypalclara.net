using Pgvector;

namespace Clara.Core.Data.Entities;

public class MemoryEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Content { get; set; } = "";
    public Vector? Embedding { get; set; }
    public string? Category { get; set; }
    public float Score { get; set; } = 1.0f;
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
}
