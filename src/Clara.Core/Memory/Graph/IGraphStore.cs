namespace Clara.Core.Memory.Graph;

/// <summary>Interface for graph-based relationship storage and retrieval.</summary>
public interface IGraphStore
{
    /// <summary>Search for entities and relationships related to a query.</summary>
    Task<List<string>> SearchAsync(
        string query, string userId, float[]? embedding = null,
        int limit = 20, CancellationToken ct = default);

    /// <summary>Add entity/relationship data to the graph.</summary>
    Task AddAsync(string data, string userId, float[]? embedding = null, CancellationToken ct = default);

    /// <summary>Get all relationships for a user.</summary>
    Task<List<string>> GetAllAsync(string userId, int limit = 50, CancellationToken ct = default);
}
