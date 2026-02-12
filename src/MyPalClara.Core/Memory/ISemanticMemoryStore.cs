namespace MyPalClara.Core.Memory;

/// <summary>
/// Unified semantic memory store interface. Combines vector and graph operations.
/// </summary>
public interface ISemanticMemoryStore
{
    // --- Vector ops ---
    Task<List<MemoryItem>> SearchAsync(float[] embedding, IReadOnlyList<string> userIds, int limit = 20, CancellationToken ct = default);
    Task InsertMemoryAsync(string id, float[] embedding, string text, string userId, Dictionary<string, object?>? metadata = null, CancellationToken ct = default);
    Task DeleteMemoryAsync(string id, CancellationToken ct = default);
    Task<List<MemoryItem>> GetAllMemoriesAsync(IReadOnlyList<string> userIds, Dictionary<string, object?>? filters = null, int limit = 100, CancellationToken ct = default);
    Task UpdateMemoryAsync(string id, float[] embedding, string newText, Dictionary<string, object?>? metadata = null, CancellationToken ct = default);
    Task<MemoryItem?> GetMemoryAsync(string id, CancellationToken ct = default);

    // --- Graph ops ---
    Task<List<string>> SearchEntitiesAsync(string query, IReadOnlyList<string> userIds, float[]? embedding = null, int limit = 20, CancellationToken ct = default);
    Task AddEntityDataAsync(string data, string userId, float[]? embedding = null, CancellationToken ct = default);
    Task<List<string>> GetAllRelationshipsAsync(IReadOnlyList<string> userIds, int limit = 50, CancellationToken ct = default);

    // --- Schema ---
    Task EnsureSchemaAsync(CancellationToken ct = default);
}
