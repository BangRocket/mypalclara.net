using MyPalClara.Core.Configuration;
using MyPalClara.Core.Llm;
using MyPalClara.Core.Memory;
using MyPalClara.Core.Modules;
using MyPalClara.Memory.Cache;
using MyPalClara.Memory.Context;
using MyPalClara.Memory.Dynamics;
using MyPalClara.Memory.Extraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory;

public sealed class MemoryModule : IGatewayModule
{
    public string Name => "Memory";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<MemoryCache>();
        services.AddHttpClient<EmbeddingClient>();
        services.AddSingleton<FalkorDbSemanticStore>();
        services.AddSingleton<ISemanticMemoryStore>(sp => sp.GetRequiredService<FalkorDbSemanticStore>());
        services.AddSingleton<MemoryDynamicsService>();
        services.AddSingleton<CompositeScorer>();
        services.AddSingleton<EmotionalContext>();
        services.AddHttpClient<RookProvider>();
        services.AddSingleton<TopicRecurrence>();
        services.AddSingleton<FactExtractor>();
        services.AddSingleton<ContradictionDetector>();
        services.AddSingleton<SmartIngest>();
        services.AddSingleton<MemoryService>();
        services.AddSingleton<IMemoryService>(sp => sp.GetRequiredService<MemoryService>());
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        var store = services.GetRequiredService<FalkorDbSemanticStore>();
        await store.EnsureSchemaAsync(ct);
        var logger = services.GetRequiredService<ILogger<MemoryModule>>();
        logger.LogInformation("Memory module initialized — FalkorDB schema ensured");
    }

    public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
}
