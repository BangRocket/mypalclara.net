namespace MyPalClara.Memory.VectorStore;

public interface IVectorStore
{
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        float[] queryVector,
        Dictionary<string, string>? filters = null,
        int limit = 100,
        float? threshold = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<MemoryPoint>> GetAllAsync(
        Dictionary<string, string>? filters = null,
        int limit = 100,
        CancellationToken ct = default);

    Task<MemoryPoint?> GetAsync(string memoryId, CancellationToken ct = default);

    Task<string> UpsertAsync(
        string? id,
        float[] vector,
        Dictionary<string, object> payload,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(string memoryId, CancellationToken ct = default);

    Task EnsureCollectionAsync(CancellationToken ct = default);
}
