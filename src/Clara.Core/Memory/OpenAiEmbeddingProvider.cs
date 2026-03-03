using System.ClientModel;
using Clara.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;

namespace Clara.Core.Memory;

public class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAiEmbeddingProvider> _logger;

    public OpenAiEmbeddingProvider(IOptions<MemoryOptions> options, ILogger<OpenAiEmbeddingProvider> logger)
    {
        _logger = logger;
        var opts = options.Value;
        var apiKey = opts.EmbeddingApiKey
                     ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                     ?? throw new InvalidOperationException("No API key configured for embeddings (set OPENAI_API_KEY or Clara:Memory:EmbeddingApiKey)");

        _client = new EmbeddingClient(opts.EmbeddingModel, apiKey);
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }
}
