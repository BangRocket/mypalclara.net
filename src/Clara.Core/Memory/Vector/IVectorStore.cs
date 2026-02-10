namespace Clara.Core.Memory.Vector;

/// <summary>Interface for vector store operations.</summary>
public interface IVectorStore
{
    /// <summary>Search by embedding similarity with optional JSONB filters.</summary>
    Task<List<MemoryItem>> SearchAsync(
        float[] embedding,
        Dictionary<string, object?>? filters = null,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>Insert a vector with JSONB payload.</summary>
    Task InsertAsync(string id, float[] embedding, Dictionary<string, object?> payload, CancellationToken ct = default);

    /// <summary>Delete by ID.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Get all matching filters (no vector search).</summary>
    Task<List<MemoryItem>> GetAllAsync(
        Dictionary<string, object?>? filters = null,
        int limit = 100,
        CancellationToken ct = default);
}
