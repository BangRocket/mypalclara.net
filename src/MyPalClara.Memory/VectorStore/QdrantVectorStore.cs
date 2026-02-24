using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace MyPalClara.Memory.VectorStore;

public sealed class QdrantVectorStore : IVectorStore
{
    private const int VectorSize = 1536;
    private const string DefaultCollectionName = "clara_memories";
    private const string DefaultQdrantUrl = "http://localhost:6333";

    private readonly QdrantClient _client;
    private readonly string _collectionName;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStore(ILogger<QdrantVectorStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _collectionName = Environment.GetEnvironmentVariable("ROOK_COLLECTION_NAME") ?? DefaultCollectionName;

        var url = Environment.GetEnvironmentVariable("QDRANT_URL") ?? DefaultQdrantUrl;
        var apiKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY");

        var uri = new Uri(url);
        _client = string.IsNullOrEmpty(apiKey)
            ? new QdrantClient(uri)
            : new QdrantClient(uri, apiKey);
    }

    public async Task EnsureCollectionAsync(CancellationToken ct = default)
    {
        try
        {
            var exists = await _client.CollectionExistsAsync(_collectionName, ct).ConfigureAwait(false);
            if (exists)
            {
                _logger.LogDebug("Qdrant collection '{Collection}' already exists", _collectionName);
                return;
            }

            await _client.CreateCollectionAsync(
                _collectionName,
                new VectorParams { Size = VectorSize, Distance = Distance.Cosine },
                cancellationToken: ct
            ).ConfigureAwait(false);

            _logger.LogInformation("Created Qdrant collection '{Collection}' ({Size}d, Cosine)",
                _collectionName, VectorSize);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure Qdrant collection '{Collection}' — Qdrant may not be running",
                _collectionName);
        }
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        float[] queryVector,
        Dictionary<string, string>? filters = null,
        int limit = 100,
        float? threshold = null,
        CancellationToken ct = default)
    {
        try
        {
            var filter = BuildFilter(filters);

            var results = await _client.SearchAsync(
                _collectionName,
                (ReadOnlyMemory<float>)queryVector,
                filter: filter,
                limit: (ulong)limit,
                scoreThreshold: threshold,
                cancellationToken: ct
            ).ConfigureAwait(false);

            return results
                .Select(sp => new MemorySearchResult(
                    ParseScoredPoint(sp),
                    sp.Score))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant search failed");
            return [];
        }
    }

    public async Task<IReadOnlyList<MemoryPoint>> GetAllAsync(
        Dictionary<string, string>? filters = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        try
        {
            var filter = BuildFilter(filters);

            var response = await _client.ScrollAsync(
                _collectionName,
                filter: filter,
                limit: (uint)limit,
                cancellationToken: ct
            ).ConfigureAwait(false);

            return response.Result
                .Select(ParseRetrievedPoint)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant scroll failed");
            return [];
        }
    }

    public async Task<MemoryPoint?> GetAsync(string memoryId, CancellationToken ct = default)
    {
        try
        {
            if (!Guid.TryParse(memoryId, out var guid))
            {
                _logger.LogWarning("Invalid memory ID format: {MemoryId}", memoryId);
                return null;
            }

            var results = await _client.RetrieveAsync(
                _collectionName,
                guid,
                withPayload: true,
                withVectors: false,
                cancellationToken: ct
            ).ConfigureAwait(false);

            return results.Count > 0 ? ParseRetrievedPoint(results[0]) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant retrieve failed for {MemoryId}", memoryId);
            return null;
        }
    }

    public async Task<string> UpsertAsync(
        string? id,
        float[] vector,
        Dictionary<string, object> payload,
        CancellationToken ct = default)
    {
        var pointId = id is not null && Guid.TryParse(id, out var existingGuid)
            ? existingGuid
            : Guid.NewGuid();

        var qdrantPayload = new Dictionary<string, Value>();
        foreach (var (key, value) in payload)
        {
            qdrantPayload[key] = ToQdrantValue(value);
        }

        var point = new PointStruct
        {
            Id = (PointId)pointId,
            Vectors = vector,
            Payload = { qdrantPayload }
        };

        await _client.UpsertAsync(
            _collectionName,
            [point],
            cancellationToken: ct
        ).ConfigureAwait(false);

        return pointId.ToString();
    }

    public async Task<bool> DeleteAsync(string memoryId, CancellationToken ct = default)
    {
        try
        {
            if (!Guid.TryParse(memoryId, out var guid))
            {
                _logger.LogWarning("Invalid memory ID format for delete: {MemoryId}", memoryId);
                return false;
            }

            await _client.DeleteAsync(
                _collectionName,
                guid,
                cancellationToken: ct
            ).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant delete failed for {MemoryId}", memoryId);
            return false;
        }
    }

    private static Filter? BuildFilter(Dictionary<string, string>? filters)
    {
        if (filters is null || filters.Count == 0)
            return null;

        var conditions = new List<Condition>();

        foreach (var (key, value) in filters)
        {
            conditions.Add(Conditions.MatchKeyword(key, value));
        }

        return new Filter { Must = { conditions } };
    }

    private static MemoryPoint ParseScoredPoint(ScoredPoint sp)
    {
        var payload = sp.Payload;
        return new MemoryPoint(
            Id: ExtractPointId(sp.Id),
            Data: GetPayloadString(payload, "data") ?? "",
            Hash: GetPayloadString(payload, "hash"),
            UserId: GetPayloadString(payload, "user_id"),
            AgentId: GetPayloadString(payload, "agent_id"),
            RunId: GetPayloadString(payload, "run_id"),
            CreatedAt: GetPayloadString(payload, "created_at"),
            UpdatedAt: GetPayloadString(payload, "updated_at"),
            IsKey: GetPayloadString(payload, "is_key") == "true",
            Metadata: null);
    }

    private static MemoryPoint ParseRetrievedPoint(RetrievedPoint rp)
    {
        var payload = rp.Payload;
        return new MemoryPoint(
            Id: ExtractPointId(rp.Id),
            Data: GetPayloadString(payload, "data") ?? "",
            Hash: GetPayloadString(payload, "hash"),
            UserId: GetPayloadString(payload, "user_id"),
            AgentId: GetPayloadString(payload, "agent_id"),
            RunId: GetPayloadString(payload, "run_id"),
            CreatedAt: GetPayloadString(payload, "created_at"),
            UpdatedAt: GetPayloadString(payload, "updated_at"),
            IsKey: GetPayloadString(payload, "is_key") == "true",
            Metadata: null);
    }

    private static string ExtractPointId(PointId pointId)
    {
        if (pointId.HasUuid)
            return pointId.Uuid;
        if (pointId.HasNum)
            return pointId.Num.ToString();
        return "";
    }

    private static string? GetPayloadString(
        Google.Protobuf.Collections.MapField<string, Value> payload,
        string key)
    {
        if (!payload.TryGetValue(key, out var value))
            return null;

        if (value.HasStringValue)
            return value.StringValue;

        if (value.HasBoolValue)
            return value.BoolValue ? "true" : "false";

        if (value.HasIntegerValue)
            return value.IntegerValue.ToString();

        if (value.HasDoubleValue)
            return value.DoubleValue.ToString();

        return null;
    }

    private static Value ToQdrantValue(object value)
    {
        return value switch
        {
            string s => (Value)s,
            bool b => (Value)b,
            int i => (Value)(long)i,
            long l => (Value)l,
            double d => (Value)d,
            float f => (Value)(double)f,
            _ => (Value)value.ToString()!
        };
    }
}
