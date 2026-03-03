namespace Clara.Core.Memory;

public record MemoryEntry(
    Guid Id,
    string UserId,
    string Content,
    string? Category,
    float Score,
    DateTime CreatedAt,
    DateTime UpdatedAt);
