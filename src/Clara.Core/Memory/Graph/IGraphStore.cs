namespace Clara.Core.Memory.Graph;

/// <summary>Interface for graph-based relationship storage and retrieval.</summary>
public interface IGraphStore
{
    /// <summary>Search for entities and relationships related to a query (across all linked user IDs).</summary>
    Task<List<string>> SearchAsync(
        string query, IReadOnlyList<string> userIds, float[]? embedding = null,
        int limit = 20, CancellationToken ct = default);

    /// <summary>Add entity/relationship data to the graph (single user, WRITE).</summary>
    Task AddAsync(string data, string userId, float[]? embedding = null, CancellationToken ct = default);

    /// <summary>Get all relationships across linked user IDs.</summary>
    Task<List<string>> GetAllAsync(IReadOnlyList<string> userIds, int limit = 50, CancellationToken ct = default);
}
