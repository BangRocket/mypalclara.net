using MyPalClara.Memory.VectorStore;

namespace MyPalClara.Memory;

public interface IRookMemory
{
    Task<MemorySearchResult[]> SearchAsync(
        string query,
        string? userId = null,
        int limit = 100,
        float? threshold = null,
        CancellationToken ct = default);

    Task<MemoryPoint[]> GetAllAsync(
        string? userId = null,
        string? agentId = null,
        int limit = 100,
        CancellationToken ct = default);

    Task<MemoryPoint?> GetAsync(string memoryId, CancellationToken ct = default);

    Task<string> CreateAsync(
        string content,
        string userId,
        bool isKey = false,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default);

    Task UpdateAsync(
        string memoryId,
        string content,
        CancellationToken ct = default);

    /// <summary>
    /// Convenience method for adding a memory with minimal parameters.
    /// Equivalent to <see cref="CreateAsync"/> without optional metadata.
    /// </summary>
    Task<string> AddAsync(
        string text,
        string userId,
        bool isKey = false,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing memory's content and optionally its user/isKey metadata.
    /// </summary>
    Task UpdateAsync(
        string memoryId,
        string content,
        string userId,
        bool? isKey = null,
        CancellationToken ct = default);

    Task DeleteAsync(string memoryId, CancellationToken ct = default);
}
