using Clara.Core.Config;
using Clara.Core.Events;
using Clara.Core.Llm;
using Clara.Core.Memory;
using Clara.Core.Prompt;
using Clara.Core.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clara.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClaraCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind config
        services.Configure<ClaraOptions>(configuration.GetSection("Clara"));
        services.Configure<LlmOptions>(configuration.GetSection("Clara:Llm"));
        services.Configure<MemoryOptions>(configuration.GetSection("Clara:Memory"));
        services.Configure<GatewayOptions>(configuration.GetSection("Clara:Gateway"));
        services.Configure<ToolOptions>(configuration.GetSection("Clara:Tools"));

        // DbContext - configured by host (Gateway decides SQLite vs PostgreSQL)

        // LLM
        services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();

        // Memory
        services.AddScoped<IMemoryStore, PgVectorMemoryStore>();
        services.AddScoped<IMemoryView, MarkdownMemoryView>();
        services.AddSingleton<IEmbeddingProvider, OpenAiEmbeddingProvider>();

        // Prompt
        services.AddSingleton<PromptComposer>();
        services.AddSingleton<IPromptSection, PersonaSection>();
        services.AddSingleton<IPromptSection, ToolConventionsSection>();
        services.AddSingleton<IPromptSection, UserContextSection>();
        services.AddSingleton<IPromptSection, MemorySection>();

        // Sessions
        services.AddScoped<ISessionManager, SessionManager>();

        // Events
        services.AddSingleton<IClaraEventBus, ClaraEventBus>();

        return services;
    }
}
