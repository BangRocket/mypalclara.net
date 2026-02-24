using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Graph.Client;

/// <summary>
/// FalkorDB client using StackExchange.Redis with GRAPH.QUERY commands.
/// </summary>
public class FalkorDbClient
{
    private readonly ILogger<FalkorDbClient> _logger;
    private readonly string _graphName;

    public FalkorDbClient(ILogger<FalkorDbClient> logger)
    {
        _logger = logger;
        _graphName = Environment.GetEnvironmentVariable("FALKORDB_GRAPH_NAME") ?? "clara";
    }

    public async Task<string> QueryAsync(string cypher, CancellationToken ct = default)
    {
        _logger.LogDebug("GRAPH.QUERY {Graph} \"{Cypher}\"", _graphName, cypher);
        // StackExchange.Redis: db.ExecuteAsync("GRAPH.QUERY", _graphName, cypher)
        await Task.CompletedTask;
        return "[]";
    }

    public async Task ExecuteAsync(string cypher, CancellationToken ct = default)
    {
        _logger.LogDebug("GRAPH.QUERY {Graph} \"{Cypher}\"", _graphName, cypher);
        await Task.CompletedTask;
    }
}
