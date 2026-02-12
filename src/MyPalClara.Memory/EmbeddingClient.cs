using System.Text;
using System.Text.Json;
using MyPalClara.Core.Configuration;
using MyPalClara.Memory.Cache;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory;

/// <summary>OpenAI-compatible text-embedding API client with optional Redis caching. Works with OpenAI, Ollama, or any compatible endpoint.</summary>
public sealed class EmbeddingClient
{
    private readonly HttpClient _http;
    private readonly ClaraConfig _config;
    private readonly ILogger<EmbeddingClient> _logger;
    private readonly MemoryCache? _cache;

    public EmbeddingClient(HttpClient http, ClaraConfig config, ILogger<EmbeddingClient> logger, MemoryCache? cache = null)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _cache = cache;

        var embeddingConfig = config.Memory.Embedding;
        var baseUrl = string.IsNullOrEmpty(embeddingConfig.BaseUrl)
            ? "https://api.openai.com/v1/"
            : embeddingConfig.BaseUrl;
        _http.BaseAddress = new Uri(baseUrl);

        // Use embedding-specific API key, fall back to global OpenAI key
        var apiKey = !string.IsNullOrEmpty(embeddingConfig.ApiKey)
            ? embeddingConfig.ApiKey
            : config.Llm.OpenaiApiKey;
        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    /// <summary>Generate embedding for a single text (cached).</summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        // Check cache first
        if (_cache is not null)
        {
            var cached = await _cache.GetEmbeddingAsync(text, ct);
            if (cached is not null)
            {
                _logger.LogDebug("Embedding cache hit");
                return cached;
            }
        }

        var result = await CallEmbeddingApiAsync(text, ct);

        // Store in cache
        if (_cache is not null)
            await _cache.SetEmbeddingAsync(text, result, ct);

        return result;
    }

    /// <summary>Generate embeddings for multiple texts.</summary>
    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        var model = _config.Memory.Embedding.Model;
        var body = new { model, input = texts };
        var json = JsonSerializer.Serialize(body);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("embeddings", content, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var data = doc.RootElement.GetProperty("data");

        var results = new float[data.GetArrayLength()][];
        foreach (var item in data.EnumerateArray())
        {
            var idx = item.GetProperty("index").GetInt32();
            var emb = item.GetProperty("embedding");
            var vec = new float[emb.GetArrayLength()];
            int j = 0;
            foreach (var val in emb.EnumerateArray())
                vec[j++] = val.GetSingle();
            results[idx] = vec;
        }

        return results;
    }

    private async Task<float[]> CallEmbeddingApiAsync(string text, CancellationToken ct)
    {
        var model = _config.Memory.Embedding.Model;
        var body = new { model, input = text };
        var json = JsonSerializer.Serialize(body);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("embeddings", content, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var embeddingArray = doc.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding");

        var result = new float[embeddingArray.GetArrayLength()];
        int i = 0;
        foreach (var val in embeddingArray.EnumerateArray())
            result[i++] = val.GetSingle();

        return result;
    }
}
