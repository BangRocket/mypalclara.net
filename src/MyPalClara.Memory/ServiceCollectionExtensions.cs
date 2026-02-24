using MyPalClara.Llm;
using MyPalClara.Memory.Embeddings;
using MyPalClara.Memory.FactExtraction;
using MyPalClara.Memory.VectorStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory;

public static class MemoryServiceCollectionExtensions
{
    public static IServiceCollection AddClaraMemory(this IServiceCollection services)
    {
        services.AddHttpClient("ClaraEmbedding");
        services.AddHttpClient("ClaraRookLlm");

        services.AddSingleton<IEmbeddingProvider>(sp =>
            new OpenAiEmbeddingProvider(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ClaraEmbedding")));

        services.AddSingleton<IVectorStore, QdrantVectorStore>();
        services.AddSingleton<IRookMemory, RookMemoryClient>();
        services.AddSingleton<SmartIngestion>();

        // IFactExtractor: uses a dedicated Rook-specific LLM provider
        services.AddSingleton<IFactExtractor>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ClaraRookLlm");
            var rookProvider = LlmFactExtractor.CreateRookProvider(httpClient);
            var logger = sp.GetRequiredService<ILogger<LlmFactExtractor>>();
            return new LlmFactExtractor(rookProvider, logger);
        });

        return services;
    }
}
