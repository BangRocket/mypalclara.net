using System.Globalization;
using System.Text;
using System.Text.Json;
using Clara.Core.Configuration;
using Clara.Core.Llm;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Clara.Core.Memory;

/// <summary>
/// Unified semantic memory store backed by FalkorDB.
/// All memory nodes, FSRS state, access events, supersessions, and entity relationships
/// live in a single FalkorDB graph.
/// </summary>
public sealed class FalkorDbSemanticStore : ISemanticMemoryStore
{
    private readonly ClaraConfig _config;
    private readonly ILogger<FalkorDbSemanticStore> _logger;
    private readonly RookProvider? _rook;
    private IConnectionMultiplexer? _redis;
    private readonly string _graphName;

    public FalkorDbSemanticStore(ClaraConfig config, ILogger<FalkorDbSemanticStore> logger, RookProvider? rook = null)
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

    // ========== Vector ops ==========

    public async Task<List<MemoryItem>> SearchAsync(
        float[] embedding, IReadOnlyList<string> userIds, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var vecStr = SerializeVecf32(embedding);
            var userFilter = BuildUserIdFilter(userIds);
            var overK = limit * 5;

            var cypher = $$"""
                CALL db.idx.vector.queryNodes('Memory', 'embedding', {{overK}}, vecf32({{vecStr}}))
                YIELD node, score
                WHERE node.user_id IN [{{userFilter}}]
                RETURN node.id AS id, node.text AS text, score,
                       node.category AS category, node.is_key AS is_key,
                       node.memory_type AS memory_type, node.topic_name AS topic_name,
                       node.emotional_weight AS emotional_weight,
                       node.channel_id AS channel_id, node.sentiment_end AS sentiment_end,
                       node.created_at AS created_at
                ORDER BY score DESC
                LIMIT {{limit}}
                """;

            var results = await ExecuteQueryAsync(db, cypher);
            var items = new List<MemoryItem>();

            foreach (var row in results)
            {
                if (row.Length < 3) continue;
                items.Add(new MemoryItem
                {
                    Id = (string?)row[0] ?? "",
                    Memory = (string?)row[1] ?? "",
                    Score = ToDouble(row[2]),
                    Metadata = BuildMetadata(row),
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB vector search failed");
            return [];
        }
    }

    public async Task InsertMemoryAsync(
        string id, float[] embedding, string text, string userId,
        Dictionary<string, object?>? metadata = null, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var vecStr = SerializeVecf32(embedding);
            var escapedText = EscapeCypher(text);
            var escapedUserId = EscapeCypher(userId);
            var escapedId = EscapeCypher(id);

            var extraProps = new StringBuilder();
            if (metadata is not null)
            {
                foreach (var (key, value) in metadata)
                {
                    if (value is null) continue;
                    var escapedValue = EscapeCypher(value.ToString() ?? "");
                    extraProps.Append($", n.{key} = '{escapedValue}'");
                }
            }

            var cypher = $$"""
                MERGE (n:Memory {id: '{{escapedId}}'})
                ON CREATE SET n.text = '{{escapedText}}',
                              n.embedding = vecf32({{vecStr}}),
                              n.user_id = '{{escapedUserId}}',
                              n.stability = 1.0,
                              n.difficulty = 5.0,
                              n.retrieval_strength = 1.0,
                              n.storage_strength = 0.5,
                              n.is_key = false,
                              n.importance_weight = 1.0,
                              n.access_count = 0,
                              n.created_at = timestamp(),
                              n.updated_at = timestamp()
                              {{extraProps}}
                ON MATCH SET n.text = '{{escapedText}}',
                             n.embedding = vecf32({{vecStr}}),
                             n.updated_at = timestamp()
                             {{extraProps}}
                """;

            await ExecuteQueryAsync(db, cypher);
            _logger.LogDebug("Inserted memory {Id} for user {UserId}", id, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB insert memory failed");
        }
    }

    public async Task DeleteMemoryAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var cypher = $"MATCH (n:Memory {{id: '{EscapeCypher(id)}'}}) DETACH DELETE n";
            await ExecuteQueryAsync(db, cypher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB delete memory failed for {Id}", id);
        }
    }

    public async Task<List<MemoryItem>> GetAllMemoriesAsync(
        IReadOnlyList<string> userIds, Dictionary<string, object?>? filters = null,
        int limit = 100, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var userFilter = BuildUserIdFilter(userIds);

            var whereClause = new StringBuilder($"n.user_id IN [{userFilter}]");
            if (filters is not null)
            {
                foreach (var (key, value) in filters)
                {
                    if (value is null) continue;
                    if (key == "user_id") continue; // already handled
                    var escapedValue = EscapeCypher(value.ToString() ?? "");
                    whereClause.Append($" AND n.{key} = '{escapedValue}'");
                }
            }

            var cypher = $$"""
                MATCH (n:Memory)
                WHERE {{whereClause}}
                RETURN n.id AS id, n.text AS text,
                       n.category AS category, n.is_key AS is_key,
                       n.memory_type AS memory_type, n.topic_name AS topic_name,
                       n.emotional_weight AS emotional_weight,
                       n.channel_id AS channel_id, n.sentiment_end AS sentiment_end,
                       n.created_at AS created_at
                LIMIT {{limit}}
                """;

            var results = await ExecuteQueryAsync(db, cypher);
            var items = new List<MemoryItem>();

            foreach (var row in results)
            {
                if (row.Length < 2) continue;
                items.Add(new MemoryItem
                {
                    Id = (string?)row[0] ?? "",
                    Memory = (string?)row[1] ?? "",
                    Score = 1.0,
                    Metadata = BuildMetadataFromGetAll(row),
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB get all memories failed");
            return [];
        }
    }

    // ========== FSRS ops ==========

    public async Task<FsrsState?> GetFsrsStateAsync(
        string memoryId, IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var userFilter = BuildUserIdFilter(userIds);

            var cypher = $$"""
                MATCH (n:Memory {id: '{{EscapeCypher(memoryId)}}'})
                WHERE n.user_id IN [{{userFilter}}]
                RETURN n.id, n.user_id, n.stability, n.difficulty,
                       n.retrieval_strength, n.storage_strength,
                       n.is_key, n.importance_weight, n.category, n.tags,
                       n.last_accessed_at, n.access_count,
                       n.created_at, n.updated_at
                """;

            var results = await ExecuteQueryAsync(db, cypher);
            if (results.Count == 0) return null;

            return ParseFsrsState(results[0]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetFsrsState failed for {MemoryId}", memoryId);
            return null;
        }
    }

    public async Task<Dictionary<string, FsrsState>> BatchGetFsrsStatesAsync(
        IEnumerable<string> memoryIds, IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var userFilter = BuildUserIdFilter(userIds);
            var idList = string.Join(", ", memoryIds.Select(id => $"'{EscapeCypher(id)}'"));

            var cypher = $$"""
                MATCH (n:Memory)
                WHERE n.id IN [{{idList}}] AND n.user_id IN [{{userFilter}}]
                RETURN n.id, n.user_id, n.stability, n.difficulty,
                       n.retrieval_strength, n.storage_strength,
                       n.is_key, n.importance_weight, n.category, n.tags,
                       n.last_accessed_at, n.access_count,
                       n.created_at, n.updated_at
                """;

            var results = await ExecuteQueryAsync(db, cypher);
            var dict = new Dictionary<string, FsrsState>();

            foreach (var row in results)
            {
                var state = ParseFsrsState(row);
                if (!string.IsNullOrEmpty(state.MemoryId))
                    dict[state.MemoryId] = state;
            }

            return dict;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BatchGetFsrsStates failed");
            return new Dictionary<string, FsrsState>();
        }
    }

    public async Task UpdateFsrsStateAsync(FsrsState state, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();

            var lastAccessed = state.LastAccessedAt.HasValue
                ? $"'{state.LastAccessedAt.Value:o}'"
                : "null";

            var cypher = $$"""
                MATCH (n:Memory {id: '{{EscapeCypher(state.MemoryId)}}'})
                SET n.stability = {{F(state.Stability)}},
                    n.difficulty = {{F(state.Difficulty)}},
                    n.retrieval_strength = {{F(state.RetrievalStrength)}},
                    n.storage_strength = {{F(state.StorageStrength)}},
                    n.is_key = {{(state.IsKey ? "true" : "false")}},
                    n.importance_weight = {{F(state.ImportanceWeight)}},
                    n.access_count = {{state.AccessCount}},
                    n.last_accessed_at = {{lastAccessed}},
                    n.updated_at = timestamp()
                """;

            if (state.Category is not null)
                cypher += $", n.category = '{EscapeCypher(state.Category)}'";
            if (state.Tags is not null)
                cypher += $", n.tags = '{EscapeCypher(state.Tags)}'";

            await ExecuteQueryAsync(db, cypher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UpdateFsrsState failed for {MemoryId}", state.MemoryId);
        }
    }

    public async Task RecordAccessEventAsync(
        string memoryId, string userId, int grade, string signalType,
        double retrievabilityAtAccess, string? context = null, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var eventId = Guid.NewGuid().ToString();

            var contextProp = context is not null
                ? $", a.context = '{EscapeCypher(context)}'"
                : "";

            var cypher = $$"""
                MATCH (m:Memory {id: '{{EscapeCypher(memoryId)}}'})
                CREATE (a:AccessEvent {
                    id: '{{eventId}}',
                    user_id: '{{EscapeCypher(userId)}}',
                    grade: {{grade}},
                    signal_type: '{{EscapeCypher(signalType)}}',
                    retrievability_at_access: {{F(retrievabilityAtAccess)}},
                    accessed_at: timestamp()
                    {{contextProp}}
                })
                CREATE (m)-[:REVIEWED]->(a)
                """;

            await ExecuteQueryAsync(db, cypher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RecordAccessEvent failed for {MemoryId}", memoryId);
        }
    }

    public async Task RecordSupersessionAsync(
        string oldId, string newId, string userId, string reason,
        double confidence, string? details = null, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();

            var detailsProp = details is not null
                ? $", details: '{EscapeCypher(details)}'"
                : "";

            var cypher = $$"""
                MATCH (old:Memory {id: '{{EscapeCypher(oldId)}}'})
                MATCH (new:Memory {id: '{{EscapeCypher(newId)}}'})
                CREATE (new)-[:SUPERSEDES {
                    reason: '{{EscapeCypher(reason)}}',
                    confidence: {{F(confidence)}}
                    {{detailsProp}},
                    created_at: timestamp()
                }]->(old)
                """;

            await ExecuteQueryAsync(db, cypher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RecordSupersession failed");
        }
    }

    // ========== Graph ops ==========

    public async Task<List<string>> SearchEntitiesAsync(
        string query, IReadOnlyList<string> userIds, float[]? embedding = null,
        int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var userFilter = BuildUserIdFilter(userIds);
            var escapedQuery = EscapeCypher(query);

            var cypher = $$"""
                MATCH (n:Entity)
                WHERE n.user_id IN [{{userFilter}}] AND toLower(n.name) CONTAINS toLower('{{escapedQuery}}')
                CALL {
                    WITH n
                    MATCH (n)-[r]->(m:Entity)
                    RETURN n.name AS source, type(r) AS rel, m.name AS target
                    UNION
                    WITH n
                    MATCH (n)<-[r]-(m:Entity)
                    RETURN m.name AS source, type(r) AS rel, n.name AS target
                }
                RETURN DISTINCT source, rel, target
                LIMIT {{limit}}
                """;

            var results = await ExecuteRelationshipQueryAsync(db, cypher);

            if (results.Count == 0)
                results = await GetAllRelationshipsAsync(userIds, limit, ct);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB entity search failed for query '{Query}'", query);
            return [];
        }
    }

    public async Task AddEntityDataAsync(
        string data, string userId, float[]? embedding = null, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();

            if (_rook is not null)
            {
                await ExtractAndStoreEntitiesAsync(db, data, userId, ct);
                return;
            }

            var entityName = data.Length > 100 ? data[..100] : data;
            var cypher = $$"""
                MERGE (n:Entity {name: '{{EscapeCypher(entityName)}}', user_id: '{{EscapeCypher(userId)}}'})
                ON CREATE SET n.created_at = timestamp()
                ON MATCH SET n.updated_at = timestamp()
                RETURN n.name
                """;

            await ExecuteQueryAsync(db, cypher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB add entity failed");
        }
    }

    public async Task<List<string>> GetAllRelationshipsAsync(
        IReadOnlyList<string> userIds, int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();
            var userFilter = BuildUserIdFilter(userIds);

            var cypher = $$"""
                MATCH (n:Entity)-[r]->(m:Entity)
                WHERE n.user_id IN [{{userFilter}}]
                RETURN n.name AS source, type(r) AS rel, m.name AS target
                LIMIT {{limit}}
                """;

            return await ExecuteRelationshipQueryAsync(db, cypher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB get all relationships failed");
            return [];
        }
    }

    // ========== Schema ==========

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        try
        {
            var db = await GetDbAsync();

            // Create vector index on Memory nodes
            var vectorIndex = """
                CREATE VECTOR INDEX FOR (m:Memory) ON (m.embedding)
                OPTIONS {dimension:1536, similarityFunction:'cosine', M:32, efConstruction:200}
                """;
            await TryExecuteAsync(db, vectorIndex);

            // Scalar indexes for Memory
            await TryExecuteAsync(db, "CREATE INDEX FOR (m:Memory) ON (m.user_id)");
            await TryExecuteAsync(db, "CREATE INDEX FOR (m:Memory) ON (m.memory_type)");
            await TryExecuteAsync(db, "CREATE INDEX FOR (m:Memory) ON (m.is_key)");

            // Scalar indexes for Entity
            await TryExecuteAsync(db, "CREATE INDEX FOR (e:Entity) ON (e.user_id)");
            await TryExecuteAsync(db, "CREATE INDEX FOR (e:Entity) ON (e.name)");

            _logger.LogInformation("FalkorDB schema ensured (vector index + scalar indexes)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FalkorDB EnsureSchema failed (indexes may already exist)");
        }
    }

    // ========== Internals ==========

    private async Task TryExecuteAsync(IDatabase db, string cypher)
    {
        try
        {
            await db.ExecuteAsync("GRAPH.QUERY", _graphName, cypher);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Schema command skipped (may already exist): {Cypher}", cypher[..Math.Min(100, cypher.Length)]);
        }
    }

    private async Task<List<RedisResult[]>> ExecuteQueryAsync(IDatabase db, string cypher)
    {
        var results = new List<RedisResult[]>();

        try
        {
            var redisResult = await db.ExecuteAsync("GRAPH.QUERY", _graphName, cypher);

            if (redisResult.Length > 1)
            {
                var dataRows = (RedisResult[])redisResult[1]!;
                foreach (var row in dataRows)
                {
                    var columns = (RedisResult[])row!;
                    results.Add(columns);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Graph query failed: {Cypher}", cypher[..Math.Min(200, cypher.Length)]);
        }

        return results;
    }

    private async Task<List<string>> ExecuteRelationshipQueryAsync(IDatabase db, string cypher)
    {
        var results = new List<string>();

        try
        {
            var redisResult = await db.ExecuteAsync("GRAPH.QUERY", _graphName, cypher);

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
            _logger.LogDebug(ex, "Relationship query failed: {Cypher}", cypher[..Math.Min(200, cypher.Length)]);
        }

        return results;
    }

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

            if (root.TryGetProperty("entities", out var entities))
            {
                foreach (var entity in entities.EnumerateArray())
                {
                    var name = entity.GetProperty("name").GetString() ?? "";
                    var type = entity.TryGetProperty("type", out var t) ? t.GetString() ?? "thing" : "thing";
                    if (string.IsNullOrEmpty(name)) continue;

                    var cypher = $$"""
                        MERGE (n:Entity {name: '{{EscapeCypher(name)}}', user_id: '{{escapedUserId}}'})
                        ON CREATE SET n.entity_type = '{{EscapeCypher(type)}}', n.created_at = timestamp()
                        ON MATCH SET n.updated_at = timestamp()
                        """;
                    await ExecuteQueryAsync(db, cypher);
                }
            }

            if (root.TryGetProperty("relationships", out var relationships))
            {
                foreach (var rel in relationships.EnumerateArray())
                {
                    var source = rel.GetProperty("source").GetString() ?? "";
                    var relType = rel.GetProperty("relationship").GetString() ?? "";
                    var target = rel.GetProperty("target").GetString() ?? "";
                    if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target) || string.IsNullOrEmpty(relType))
                        continue;

                    var safeRelType = new string(relType.Select(c =>
                        char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_').ToArray());

                    var cypher = $$"""
                        MATCH (a:Entity {name: '{{EscapeCypher(source)}}', user_id: '{{escapedUserId}}'})
                        MATCH (b:Entity {name: '{{EscapeCypher(target)}}', user_id: '{{escapedUserId}}'})
                        MERGE (a)-[:{{safeRelType}}]->(b)
                        """;
                    await ExecuteQueryAsync(db, cypher);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM entity extraction failed, falling back to simple entity");

            var entityName = data.Length > 100 ? data[..100] : data;
            var cypher = $$"""
                MERGE (n:Entity {name: '{{EscapeCypher(entityName)}}', user_id: '{{EscapeCypher(userId)}}'})
                ON CREATE SET n.created_at = timestamp()
                ON MATCH SET n.updated_at = timestamp()
                """;
            await ExecuteQueryAsync(db, cypher);
        }
    }

    private static FsrsState ParseFsrsState(RedisResult[] row)
    {
        return new FsrsState
        {
            MemoryId = (string?)row[0] ?? "",
            UserId = (string?)row[1] ?? "",
            Stability = ToDouble(row[2], 1.0),
            Difficulty = ToDouble(row[3], 5.0),
            RetrievalStrength = ToDouble(row[4], 1.0),
            StorageStrength = ToDouble(row[5], 0.5),
            IsKey = ToBool(row[6]),
            ImportanceWeight = ToDouble(row[7], 1.0),
            Category = (string?)row[8],
            Tags = (string?)row[9],
            LastAccessedAt = ToDateTime(row[10]),
            AccessCount = ToInt(row[11]),
        };
    }

    private static Dictionary<string, object?> BuildMetadata(RedisResult[] row)
    {
        var meta = new Dictionary<string, object?>();
        if (row.Length > 3) meta["category"] = (string?)row[3];
        if (row.Length > 4) meta["is_key"] = (string?)row[4];
        if (row.Length > 5) meta["memory_type"] = (string?)row[5];
        if (row.Length > 6) meta["topic_name"] = (string?)row[6];
        if (row.Length > 7) meta["emotional_weight"] = (string?)row[7];
        if (row.Length > 8) meta["channel_id"] = (string?)row[8];
        if (row.Length > 9) meta["sentiment_end"] = (string?)row[9];
        if (row.Length > 10) meta["created_at"] = (string?)row[10];
        return meta;
    }

    private static Dictionary<string, object?> BuildMetadataFromGetAll(RedisResult[] row)
    {
        var meta = new Dictionary<string, object?>();
        if (row.Length > 2) meta["category"] = (string?)row[2];
        if (row.Length > 3) meta["is_key"] = (string?)row[3];
        if (row.Length > 4) meta["memory_type"] = (string?)row[4];
        if (row.Length > 5) meta["topic_name"] = (string?)row[5];
        if (row.Length > 6) meta["emotional_weight"] = (string?)row[6];
        if (row.Length > 7) meta["channel_id"] = (string?)row[7];
        if (row.Length > 8) meta["sentiment_end"] = (string?)row[8];
        if (row.Length > 9) meta["created_at"] = (string?)row[9];
        return meta;
    }

    private static string SerializeVecf32(float[] embedding)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < embedding.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(embedding[i].ToString("G9", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string BuildUserIdFilter(IReadOnlyList<string> userIds)
        => string.Join(", ", userIds.Select(id => $"'{EscapeCypher(id)}'"));

    private static string EscapeCypher(string value)
        => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static double ToDouble(RedisResult? result, double defaultValue = 0.0)
    {
        if (result is null || result.IsNull) return defaultValue;
        var str = result.ToString();
        return double.TryParse(str, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    }

    private static int ToInt(RedisResult? result, int defaultValue = 0)
    {
        if (result is null || result.IsNull) return defaultValue;
        var str = result.ToString();
        return int.TryParse(str, out var v) ? v : defaultValue;
    }

    private static bool ToBool(RedisResult? result)
    {
        if (result is null || result.IsNull) return false;
        var str = result.ToString()?.ToLowerInvariant();
        return str is "true" or "1";
    }

    private static DateTime? ToDateTime(RedisResult? result)
    {
        if (result is null || result.IsNull) return null;
        var str = result.ToString();
        if (string.IsNullOrEmpty(str) || str == "null") return null;
        return DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt : null;
    }

    /// <summary>Format double for Cypher with invariant culture.</summary>
    private static string F(double value)
        => value.ToString("G", CultureInfo.InvariantCulture);
}
