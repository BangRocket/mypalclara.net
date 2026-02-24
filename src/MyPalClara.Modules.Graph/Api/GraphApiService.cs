using MyPalClara.Modules.Graph.Client;
using MyPalClara.Modules.Graph.Models;

namespace MyPalClara.Modules.Graph.Api;

public class GraphApiService
{
    private readonly GraphOperations _ops;

    public GraphApiService(GraphOperations ops) => _ops = ops;

    public Task<List<GraphEntity>> GetEntitiesAsync(string? type, int limit, CancellationToken ct)
        => _ops.GetEntitiesAsync(type, limit, ct);

    public Task<List<GraphRelationship>> GetRelationshipsAsync(string? type, int limit, CancellationToken ct)
        => _ops.GetRelationshipsAsync(type, limit, ct);
}
