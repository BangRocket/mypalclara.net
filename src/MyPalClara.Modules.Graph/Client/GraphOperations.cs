using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Graph.Models;

namespace MyPalClara.Modules.Graph.Client;

public class GraphOperations
{
    private readonly FalkorDbClient _client;
    private readonly ILogger<GraphOperations> _logger;

    public GraphOperations(FalkorDbClient client, ILogger<GraphOperations> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task UpsertEntityAsync(string name, string type, CancellationToken ct = default)
    {
        var cypher = $"MERGE (e:{type} {{name: '{EscapeCypher(name)}'}}) SET e.updated_at = timestamp()";
        await _client.ExecuteAsync(cypher, ct);
    }

    public async Task UpsertRelationshipAsync(string subject, string predicate, string @object,
        CancellationToken ct = default)
    {
        var cypher = $$"""
            MERGE (s {name: '{{EscapeCypher(subject)}}'})
            MERGE (o {name: '{{EscapeCypher(@object)}}'})
            MERGE (s)-[r:{{EscapeCypher(predicate)}}]->(o)
            SET r.updated_at = timestamp()
            """;
        await _client.ExecuteAsync(cypher, ct);
    }

    public async Task<List<GraphEntity>> GetEntitiesAsync(string? type = null, int limit = 50,
        CancellationToken ct = default)
    {
        var filter = type is not null ? $"WHERE labels(e) CONTAINS '{EscapeCypher(type)}'" : "";
        var cypher = $"MATCH (e) {filter} RETURN e LIMIT {limit}";
        await _client.QueryAsync(cypher, ct);
        return [];
    }

    public async Task<List<GraphRelationship>> GetRelationshipsAsync(string? type = null, int limit = 50,
        CancellationToken ct = default)
    {
        var filter = type is not null ? $"WHERE type(r) = '{EscapeCypher(type)}'" : "";
        var cypher = $"MATCH ()-[r]->() {filter} RETURN r LIMIT {limit}";
        await _client.QueryAsync(cypher, ct);
        return [];
    }

    private static string EscapeCypher(string s) => s.Replace("'", "\\'");
}
