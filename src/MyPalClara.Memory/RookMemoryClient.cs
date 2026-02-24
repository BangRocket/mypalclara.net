using System.Security.Cryptography;
using System.Text;
using MyPalClara.Memory.Embeddings;
using MyPalClara.Memory.VectorStore;

namespace MyPalClara.Memory;

public sealed class RookMemoryClient : IRookMemory
{
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;

    public RookMemoryClient(IEmbeddingProvider embeddings, IVectorStore vectorStore)
    {
        _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
    }

    public async Task<MemorySearchResult[]> SearchAsync(
        string query,
        string? userId = null,
        int limit = 100,
        float? threshold = null,
        CancellationToken ct = default)
    {
        var vector = await _embeddings.EmbedAsync(query, ct).ConfigureAwait(false);

        var filters = new Dictionary<string, string>();
        if (userId is not null)
            filters["user_id"] = userId;

        var results = await _vectorStore.SearchAsync(vector, filters, limit, threshold, ct).ConfigureAwait(false);
        return [.. results];
    }

    public async Task<MemoryPoint[]> GetAllAsync(
        string? userId = null,
        string? agentId = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var filters = new Dictionary<string, string>();
        if (userId is not null)
            filters["user_id"] = userId;
        if (agentId is not null)
            filters["agent_id"] = agentId;

        var results = await _vectorStore.GetAllAsync(filters, limit, ct).ConfigureAwait(false);
        return [.. results];
    }

    public async Task<MemoryPoint?> GetAsync(string memoryId, CancellationToken ct = default)
    {
        return await _vectorStore.GetAsync(memoryId, ct).ConfigureAwait(false);
    }

    public async Task<string> CreateAsync(
        string content,
        string userId,
        bool isKey = false,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        var vector = await _embeddings.EmbedAsync(content, ct).ConfigureAwait(false);
        var now = DateTime.UtcNow.ToString("o");

        var payload = new Dictionary<string, object>
        {
            ["data"] = content,
            ["hash"] = ComputeMd5(content),
            ["user_id"] = userId,
            ["is_key"] = isKey ? "true" : "false",
            ["created_at"] = now,
            ["updated_at"] = now,
        };

        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                // Don't overwrite core fields
                if (!payload.ContainsKey(key))
                    payload[key] = value;
            }
        }

        return await _vectorStore.UpsertAsync(null, vector, payload, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(
        string memoryId,
        string content,
        CancellationToken ct = default)
    {
        var existing = await _vectorStore.GetAsync(memoryId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Memory {memoryId} not found.");

        var vector = await _embeddings.EmbedAsync(content, ct).ConfigureAwait(false);
        var now = DateTime.UtcNow.ToString("o");

        var payload = new Dictionary<string, object>
        {
            ["data"] = content,
            ["hash"] = ComputeMd5(content),
            ["user_id"] = existing.UserId ?? "",
            ["is_key"] = existing.IsKey ? "true" : "false",
            ["created_at"] = existing.CreatedAt ?? now,
            ["updated_at"] = now,
        };

        if (existing.AgentId is not null)
            payload["agent_id"] = existing.AgentId;
        if (existing.RunId is not null)
            payload["run_id"] = existing.RunId;

        await _vectorStore.UpsertAsync(memoryId, vector, payload, ct).ConfigureAwait(false);
    }

    public Task<string> AddAsync(
        string text,
        string userId,
        bool isKey = false,
        CancellationToken ct = default)
    {
        return CreateAsync(text, userId, isKey, metadata: null, ct);
    }

    public async Task UpdateAsync(
        string memoryId,
        string content,
        string userId,
        bool? isKey = null,
        CancellationToken ct = default)
    {
        var existing = await _vectorStore.GetAsync(memoryId, ct).ConfigureAwait(false);

        var vector = await _embeddings.EmbedAsync(content, ct).ConfigureAwait(false);
        var now = DateTime.UtcNow.ToString("o");

        var effectiveIsKey = isKey ?? existing?.IsKey ?? false;
        var createdAt = existing?.CreatedAt ?? now;

        var payload = new Dictionary<string, object>
        {
            ["data"] = content,
            ["hash"] = ComputeMd5(content),
            ["user_id"] = userId,
            ["is_key"] = effectiveIsKey ? "true" : "false",
            ["created_at"] = createdAt,
            ["updated_at"] = now,
        };

        if (existing?.AgentId is not null)
            payload["agent_id"] = existing.AgentId;
        if (existing?.RunId is not null)
            payload["run_id"] = existing.RunId;

        await _vectorStore.UpsertAsync(memoryId, vector, payload, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string memoryId, CancellationToken ct = default)
    {
        var deleted = await _vectorStore.DeleteAsync(memoryId, ct).ConfigureAwait(false);
        if (!deleted)
            throw new InvalidOperationException($"Failed to delete memory {memoryId}.");
    }

    private static string ComputeMd5(string content)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }
}
