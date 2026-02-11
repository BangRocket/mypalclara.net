using System.Text.Json;
using Clara.Core.Configuration;
using Clara.Core.Llm;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Clara.Core.Memory.Graph;

/// <summary>
/// FalkorDB graph store using Redis protocol with GRAPH.QUERY commands.
/// Port of clara_core/memory/graph/falkordb.py.
/// Entity nodes use :__Entity__ label with user_id, name, entity_type properties.
/// Relationships formatted as "source -> relationship -> destination".
/// </summary>
public sealed class FalkorDbStore : IGraphStore
{
    private readonly ClaraConfig _config;
    private readonly ILogger<FalkorDbStore> _logger;
    private readonly RookProvider? _rook;
    private IConnectionMultiplexer? _redis;
    private readonly string _graphName;

    public FalkorDbStore(ClaraConfig config, ILogger<FalkorDbStore> logger, RookProvider? rook = null)
    {
        _config = config;
        _logger = logger;
        _rook = rook;
        _graphName = config.Memory.GraphStore.FalkordbGraphName;
    }

    private async Task<IDatabase> GetDbAsync()
    {
        if (_redis is null)
        {
            var graphConfig = _config.Memory.GraphStore;
            var options = new ConfigurationOptions
            {
                EndPoints = { { graphConfig.FalkordbHost, graphConfig.FalkordbPort } },
                AbortOnConnectFail = false,
                ConnectTimeout = 5000,
            };

            if (!string.IsNullOrEmpty(graphConfig.FalkordbPassword))
                options.Password = graphConfig.FalkordbPassword;

            _redis = await ConnectionMultiplexer.ConnectAsync(options);
            _logger.LogInformation("Connected to FalkorDB at {Host}:{Port}",
                graphConfig.FalkordbHost, graphConfig.FalkordbPort);
        }

        return _redis.GetDatabase();
    }

    public async Task<List<string>> SearchAsync(
        string query, IReadOnlyList<string> userIds, float[]? embedding = null,
        int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var userIdFilter = BuildUserIdFilter(userIds);
            var escapedQuery = EscapeCypher(query);

            var cypher = $$"""
                MATCH (n:__Entity__)
                WHERE n.user_id IN [{{userIdFilter}}] AND toLower(n.name) CONTAINS toLower('{{escapedQuery}}')
                CALL {
                    WITH n
                    MATCH (n)-[r]->(m:__Entity__)
                    RETURN n.name AS source, type(r) AS rel, m.name AS target
                    UNION
                    WITH n
                    MATCH (n)<-[r]-(m:__Entity__)
                    RETURN m.name AS source, type(r) AS rel, n.name AS target
                }
                RETURN DISTINCT source, rel, target
                LIMIT {{limit}}
                """;

            var results = await ExecuteGraphQueryAsync(db, cypher);

            // If no results from name search, get all relationships for user
            if (results.Count == 0)
                results = await GetAllAsync(userIds, limit, ct);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB search failed for query '{Query}'", query);
            return [];
        }
    }

    public async Task AddAsync(string data, string userId, float[]? embedding = null, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();

            // If we have a Rook provider, use LLM to extract entities and relationships
            if (_rook is not null)
            {
                await ExtractAndStoreEntitiesAsync(db, data, userId, ct);
                return;
            }

            // Fallback: store as a single entity node
            var entityName = data.Length > 100 ? data[..100] : data;
            var escapedName = EscapeCypher(entityName);
            var escapedUserId = EscapeCypher(userId);

            var cypher = $$"""
                MERGE (n:__Entity__ {name: '{{escapedName}}', user_id: '{{escapedUserId}}'})
                ON CREATE SET n.created_at = timestamp()
                ON MATCH SET n.updated_at = timestamp()
                RETURN n.name
                """;

            await ExecuteGraphQueryAsync(db, cypher);
            _logger.LogDebug("Added entity to graph: {Entity}", entityName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB add failed");
        }
    }

    public async Task<List<string>> GetAllAsync(IReadOnlyList<string> userIds, int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var userIdFilter = BuildUserIdFilter(userIds);

            var cypher = $$"""
                MATCH (n:__Entity__)-[r]->(m:__Entity__)
                WHERE n.user_id IN [{{userIdFilter}}]
                RETURN n.name AS source, type(r) AS rel, m.name AS target
                LIMIT {{limit}}
                """;

            return await ExecuteGraphQueryAsync(db, cypher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB get all failed");
            return [];
        }
    }

    /// <summary>Build a Cypher-compatible list of quoted, escaped user IDs.</summary>
    private static string BuildUserIdFilter(IReadOnlyList<string> userIds)
        => string.Join(", ", userIds.Select(id => $"'{EscapeCypher(id)}'"));

    /// <summary>LLM-based entity/relationship extraction, then Cypher MERGE.</summary>
    private async Task ExtractAndStoreEntitiesAsync(
        IDatabase db, string data, string userId, CancellationToken ct)
    {
        var prompt = $$"""
            Extract entities and relationships from this text.
            Return a JSON object with:
            - "entities": array of {"name": "...", "type": "person|place|thing|concept"}
            - "relationships": array of {"source": "...", "relationship": "...", "target": "..."}

            Text: {{data}}

            Return ONLY a JSON object, no other text.
            """;

        try
        {
            var response = await _rook!.CompleteAsync(
                [new SystemMessage("You extract entities and relationships from text. Return only JSON."),
                 new UserMessage(prompt)],
                ct: ct);

            var trimmed = response.Trim();
            if (trimmed.StartsWith("```"))
            {
                var lines = trimmed.Split('\n');
                trimmed = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
            }

            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            var escapedUserId = EscapeCypher(userId);

            // Create entity nodes
            if (root.TryGetProperty("entities", out var entities))
            {
                foreach (var entity in entities.EnumerateArray())
                {
                    var name = entity.GetProperty("name").GetString() ?? "";
                    var type = entity.TryGetProperty("type", out var t) ? t.GetString() ?? "thing" : "thing";
                    if (string.IsNullOrEmpty(name)) continue;

                    var cypher = $$"""
                        MERGE (n:__Entity__ {name: '{{EscapeCypher(name)}}', user_id: '{{escapedUserId}}'})
                        ON CREATE SET n.entity_type = '{{EscapeCypher(type)}}', n.created_at = timestamp()
                        ON MATCH SET n.updated_at = timestamp()
                        """;
                    await ExecuteGraphQueryAsync(db, cypher);
                }
            }

            // Create relationship edges
            if (root.TryGetProperty("relationships", out var relationships))
            {
                foreach (var rel in relationships.EnumerateArray())
                {
                    var source = rel.GetProperty("source").GetString() ?? "";
                    var relType = rel.GetProperty("relationship").GetString() ?? "";
                    var target = rel.GetProperty("target").GetString() ?? "";
                    if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target) || string.IsNullOrEmpty(relType))
                        continue;

                    // Sanitize relationship type for Cypher (alphanumeric + underscore only)
                    var safeRelType = new string(relType.Select(c =>
                        char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_').ToArray());

                    var cypher = $$"""
                        MATCH (a:__Entity__ {name: '{{EscapeCypher(source)}}', user_id: '{{escapedUserId}}'})
                        MATCH (b:__Entity__ {name: '{{EscapeCypher(target)}}', user_id: '{{escapedUserId}}'})
                        MERGE (a)-[:{{safeRelType}}]->(b)
                        """;
                    await ExecuteGraphQueryAsync(db, cypher);
                }
            }

            _logger.LogDebug("Extracted and stored entities/relationships from graph data");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM entity extraction failed, falling back to simple entity");

            // Fallback: store as simple entity
            var entityName = data.Length > 100 ? data[..100] : data;
            var cypher = $$"""
                MERGE (n:__Entity__ {name: '{{EscapeCypher(entityName)}}', user_id: '{{EscapeCypher(userId)}}'})
                ON CREATE SET n.created_at = timestamp()
                ON MATCH SET n.updated_at = timestamp()
                """;
            await ExecuteGraphQueryAsync(db, cypher);
        }
    }

    private async Task<List<string>> ExecuteGraphQueryAsync(IDatabase db, string cypher)
    {
        var results = new List<string>();

        try
        {
            var redisResult = await db.ExecuteAsync("GRAPH.QUERY", _graphName, cypher);
            var resultStr = redisResult.ToString();

            // FalkorDB returns a nested array structure via Redis protocol.
            // The result is an array: [header_row, data_rows, metadata].
            // We parse using the RedisResult indexer which returns nested results.
            if (redisResult.Length > 1)
            {
                var dataRows = (RedisResult[])redisResult[1]!;
                foreach (var row in dataRows)
                {
                    var columns = (RedisResult[])row!;
                    if (columns.Length >= 3)
                    {
                        var source = (string?)columns[0] ?? "";
                        var rel = (string?)columns[1] ?? "";
                        var target = (string?)columns[2] ?? "";
                        results.Add($"{source} \u2192 {rel} \u2192 {target}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Graph query failed: {Cypher}", cypher[..Math.Min(200, cypher.Length)]);
        }

        return results;
    }

    private static string EscapeCypher(string value)
        => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
