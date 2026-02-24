using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyPalClara.Memory.Embeddings;

public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private const string DefaultEndpoint = "https://api.openai.com/v1/embeddings";
    private const string DefaultModel = "text-embedding-3-small";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public OpenAiEmbeddingProvider(HttpClient http, string? apiKey = null, string? model = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "OPENAI_API_KEY environment variable is not set and no API key was provided.");
        _model = model ?? DefaultModel;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var result = await EmbedBatchAsync([text], ct).ConfigureAwait(false);
        return result[0];
    }

    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
            throw new ArgumentException("At least one text must be provided.", nameof(texts));

        object input = texts.Count == 1 ? (object)texts[0] : texts;
        var requestBody = new EmbeddingRequest(input, _model);
        var json = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, DefaultEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<EmbeddingResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize embedding response.");

        if (result.Data is null || result.Data.Count == 0)
            throw new InvalidOperationException("Embedding response contained no data.");

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToArray();
    }

    private sealed record EmbeddingRequest(object Input, string Model);

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
