using Clara.Core.Config;
using Clara.Core.Events;
using Clara.Core.Llm;
using Clara.Core.Memory;
using Clara.Core.Prompt;
using Clara.Core.Sessions;
using Clara.Core.SubAgents;
using Clara.Core.Tools;
using Clara.Core.Tools.BuiltIn;
using Clara.Core.Tools.Mcp;
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

        // Sessions — singleton because SessionManager creates its own scopes internally
        services.AddSingleton<ISessionManager, SessionManager>();

        // Events
        services.AddSingleton<IClaraEventBus, ClaraEventBus>();

        // MCP client infrastructure
        services.AddSingleton<McpServerManager>();
        services.AddSingleton<McpRegistryAdapter>();
        services.AddSingleton<McpInstaller>();
        services.AddSingleton<McpOAuthHandler>();

        // Email service (stub — replaced by gateway module when configured)
        services.AddSingleton<IEmailService, StubEmailService>();

        // Sub-agents
        services.Configure<SubAgentOptions>(configuration.GetSection("Clara:SubAgents"));
        services.AddSingleton<ISubAgentManager, SubAgentManager>();

        return services;
    }

    /// <summary>
    /// Register all built-in tools into the tool registry.
    /// Call this after building the service provider.
    /// </summary>
    public static void RegisterBuiltInTools(this IServiceProvider services)
    {
        var registry = services.GetRequiredService<IToolRegistry>();
        var httpClient = new HttpClient();

        // File tools
        registry.Register(new FileReadTool());
        registry.Register(new FileWriteTool());
        registry.Register(new FileListTool());
        registry.Register(new FileDeleteTool());

        // GitHub tools
        registry.Register(new GitHubListReposTool(httpClient));
        registry.Register(new GitHubGetIssuesTool(httpClient));
        registry.Register(new GitHubGetPullRequestsTool(httpClient));
        registry.Register(new GitHubCreateIssueTool(httpClient));
        registry.Register(new GitHubGetFileTool(httpClient));

        // Azure DevOps tools
        registry.Register(new AzDoListReposTool(httpClient));
        registry.Register(new AzDoGetWorkItemsTool(httpClient));
        registry.Register(new AzDoGetPipelinesTool(httpClient));

        // Google Workspace tools
        registry.Register(new GoogleListFilesTool(httpClient));
        registry.Register(new GoogleReadSheetTool(httpClient));
        registry.Register(new GoogleCreateDocTool(httpClient));

        // Email tools
        var emailService = services.GetRequiredService<IEmailService>();
        registry.Register(new EmailCheckTool(emailService));
        registry.Register(new EmailReadTool(emailService));
        registry.Register(new EmailSendAlertTool(emailService));

        // Session tools
        var sessionManager = services.GetRequiredService<ISessionManager>();
        registry.Register(new SessionsListTool(sessionManager));
        registry.Register(new SessionsHistoryTool(sessionManager));
        registry.Register(new SessionsSendTool(sessionManager));

        // Sub-agent tools
        var subAgentManager = services.GetRequiredService<ISubAgentManager>();
        registry.Register(new SessionsSpawnTool(subAgentManager));
    }
}
