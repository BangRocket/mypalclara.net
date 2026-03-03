namespace Clara.Core.Memory;

public interface IMemoryStore
{
    Task StoreAsync(string userId, string content, MemoryMetadata? metadata = null, CancellationToken ct = default);
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(string userId, string query, int limit = 10, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryEntry>> GetAllAsync(string userId, CancellationToken ct = default);
    Task DeleteAsync(string userId, Guid memoryId, CancellationToken ct = default);
}
