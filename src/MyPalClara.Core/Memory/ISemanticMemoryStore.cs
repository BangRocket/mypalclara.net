namespace MyPalClara.Core.Memory;

/// <summary>
/// Unified semantic memory store interface. Combines vector, FSRS, and graph operations.
/// </summary>
public interface ISemanticMemoryStore
{
    // --- Vector ops ---
    Task<List<MemoryItem>> SearchAsync(float[] embedding, IReadOnlyList<string> userIds, int limit = 20, CancellationToken ct = default);
    Task InsertMemoryAsync(string id, float[] embedding, string text, string userId, Dictionary<string, object?>? metadata = null, CancellationToken ct = default);
    Task DeleteMemoryAsync(string id, CancellationToken ct = default);
    Task<List<MemoryItem>> GetAllMemoriesAsync(IReadOnlyList<string> userIds, Dictionary<string, object?>? filters = null, int limit = 100, CancellationToken ct = default);

    // --- FSRS ops ---
    Task<FsrsState?> GetFsrsStateAsync(string memoryId, IReadOnlyList<string> userIds, CancellationToken ct = default);
    Task<Dictionary<string, FsrsState>> BatchGetFsrsStatesAsync(IEnumerable<string> memoryIds, IReadOnlyList<string> userIds, CancellationToken ct = default);
    Task UpdateFsrsStateAsync(FsrsState state, CancellationToken ct = default);
    Task RecordAccessEventAsync(string memoryId, string userId, int grade, string signalType, double retrievabilityAtAccess, string? context = null, CancellationToken ct = default);
    Task RecordSupersessionAsync(string oldId, string newId, string userId, string reason, double confidence, string? details = null, CancellationToken ct = default);

    // --- Graph ops ---
    Task<List<string>> SearchEntitiesAsync(string query, IReadOnlyList<string> userIds, float[]? embedding = null, int limit = 20, CancellationToken ct = default);
    Task AddEntityDataAsync(string data, string userId, float[]? embedding = null, CancellationToken ct = default);
    Task<List<string>> GetAllRelationshipsAsync(IReadOnlyList<string> userIds, int limit = 50, CancellationToken ct = default);

    // --- Schema ---
    Task EnsureSchemaAsync(CancellationToken ct = default);
}
