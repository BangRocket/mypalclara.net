namespace MyPalClara.Core.Memory;

public sealed record MemoryHistoryEntry(
    string Id,
    string MemoryId,
    string? UserId,
    string? OldMemory,
    string? NewMemory,
    string Event,
    DateTime CreatedAt);
