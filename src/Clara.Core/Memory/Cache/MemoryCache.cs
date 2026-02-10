using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Memory.Cache;

/// <summary>
/// Redis cache layer for search results and embeddings.
/// Gracefully degrades if Redis is unavailable.
/// </summary>
public sealed class MemoryCache
{
    private readonly IDistributedCache? _cache;
    private readonly ILogger<MemoryCache> _logger;
    private bool _available = true;

    public MemoryCache(ILogger<MemoryCache> logger, IDistributedCache? cache = null)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Get cached embedding for a text key.</summary>
    public async Task<float[]?> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (_cache is null || !_available) return null;

        try
        {
            var key = $"emb:{HashKey(text)}";
            var data = await _cache.GetStringAsync(key, ct);
            return data is not null ? JsonSerializer.Deserialize<float[]>(data) : null;
        }
        catch (Exception ex)
        {
            HandleCacheError(ex);
            return null;
        }
    }

    /// <summary>Cache an embedding.</summary>
    public async Task SetEmbeddingAsync(string text, float[] embedding, CancellationToken ct = default)
    {
        if (_cache is null || !_available) return;

        try
        {
            var key = $"emb:{HashKey(text)}";
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
            };
            await _cache.SetStringAsync(key, JsonSerializer.Serialize(embedding), options, ct);
        }
        catch (Exception ex)
        {
            HandleCacheError(ex);
        }
    }

    /// <summary>Get cached search results.</summary>
    public async Task<string?> GetSearchResultAsync(string query, string userId, CancellationToken ct = default)
    {
        if (_cache is null || !_available) return null;

        try
        {
            var key = $"search:{userId}:{HashKey(query)}";
            return await _cache.GetStringAsync(key, ct);
        }
        catch (Exception ex)
        {
            HandleCacheError(ex);
            return null;
        }
    }

    /// <summary>Cache search results (5 min TTL).</summary>
    public async Task SetSearchResultAsync(string query, string userId, string json, CancellationToken ct = default)
    {
        if (_cache is null || !_available) return;

        try
        {
            var key = $"search:{userId}:{HashKey(query)}";
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            };
            await _cache.SetStringAsync(key, json, options, ct);
        }
        catch (Exception ex)
        {
            HandleCacheError(ex);
        }
    }

    private void HandleCacheError(Exception ex)
    {
        if (_available)
        {
            _logger.LogWarning(ex, "Redis cache unavailable, degrading gracefully");
            _available = false;
        }
    }

    private static string HashKey(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes)[..16];
    }
}
