using System.Collections.Concurrent;

namespace MyPalClara.Modules.Graph.Cache;

public class GraphCache
{
    private readonly ConcurrentDictionary<string, (object Value, DateTime ExpiresAt)> _cache = new();

    public T? Get<T>(string key) where T : class
    {
        if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            return entry.Value as T;
        _cache.TryRemove(key, out _);
        return null;
    }

    public void Set(string key, object value, TimeSpan ttl)
    {
        _cache[key] = (value, DateTime.UtcNow + ttl);
    }

    public void Invalidate(string key) => _cache.TryRemove(key, out _);
}
