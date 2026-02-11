using Clara.Cli;
using Clara.Cli.Repl;
using Clara.Core.Configuration;
using Clara.Core.Data;
using Clara.Core.Identity;
using Clara.Core.Llm;
using Clara.Core.Mcp;
using Clara.Core.Memory;
using Clara.Core.Memory.Cache;
using Clara.Core.Memory.Context;
using Clara.Core.Memory.Dynamics;
using Clara.Core.Memory.Extraction;
using Clara.Core.Memory.Graph;
using Clara.Core.Memory.Vector;
using Clara.Core.Orchestration;
using Clara.Core.Personality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

// Resolve config path from CLI args or env
var configPath = args.Length > 0 && args[0] == "--config" && args.Length > 1
    ? args[1]
    : null;

var yamlPath = ConfigLoader.ResolveConfigPath(configPath);
var console = AnsiConsole.Console;

ClaraConfig config;
try
{
    config = ConfigLoader.Load(yamlPath);
}
catch (Exception ex)
{
    console.MarkupLine($"[red]Failed to load config: {ex.Message.EscapeMarkup()}[/]");
    return 1;
}

Banner.Print(console);
console.MarkupLine($"[dim]Provider: {config.Llm.Provider.EscapeMarkup()}, Model: {config.Llm.ActiveProvider.Model.EscapeMarkup()}[/]");

// Build host with DI
var builder = Host.CreateApplicationBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);

// --- Core singletons ---
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<IAnsiConsole>(console);

// --- LLM provider ---
builder.Services.AddHttpClient<AnthropicProvider>();
builder.Services.AddSingleton<ILlmProvider>(sp => sp.GetRequiredService<AnthropicProvider>());

// --- Rook provider (OpenAI-compatible, for fact extraction / topic extraction / graph entities) ---
builder.Services.AddHttpClient<RookProvider>();

// --- MCP ---
builder.Services.AddSingleton<McpConfigLoader>();
builder.Services.AddSingleton<McpServerManager>();

// --- Orchestration ---
builder.Services.AddSingleton<ToolExecutor>();
builder.Services.AddSingleton<LlmOrchestrator>();

// --- Personality ---
builder.Services.AddSingleton<PersonalityLoader>();

// --- Embedding client ---
builder.Services.AddHttpClient<EmbeddingClient>();

// --- Vector store ---
builder.Services.AddSingleton<IVectorStore, PgVectorStore>();

// --- EF Core (FSRS tables + identity) ---
if (!string.IsNullOrEmpty(config.Database.Url))
{
    builder.Services.AddDbContextFactory<ClaraDbContext>(options =>
        options.UseNpgsql(config.Database.Url));
    builder.Services.AddSingleton<UserIdentityService>();
}

// --- FSRS / memory dynamics ---
builder.Services.AddSingleton<MemoryDynamicsService>();
builder.Services.AddSingleton<CompositeScorer>();

// --- Memory extraction ---
builder.Services.AddSingleton<ContradictionDetector>();
builder.Services.AddSingleton<FactExtractor>();
builder.Services.AddSingleton<SmartIngest>();

// --- Graph store (optional, only if enabled) ---
if (config.Memory.GraphStore.Enabled)
{
    builder.Services.AddSingleton<IGraphStore, FalkorDbStore>();
}

// --- Emotional context & topic recurrence ---
builder.Services.AddSingleton<EmotionalContext>();
builder.Services.AddSingleton<TopicRecurrence>();

// --- Redis cache (optional) ---
if (!string.IsNullOrEmpty(config.Memory.RedisUrl))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = config.Memory.RedisUrl;
        options.InstanceName = "clara:";
    });
}
builder.Services.AddSingleton<MemoryCache>();

// --- Memory service (top-level orchestrator) ---
builder.Services.AddSingleton<MemoryService>();

// --- REPL ---
builder.Services.AddSingleton<StreamingRenderer>();
builder.Services.AddSingleton<CommandDispatcher>();
builder.Services.AddSingleton<ChatRepl>();

var host = builder.Build();

// Ensure EF Core tables exist
if (!string.IsNullOrEmpty(config.Database.Url))
{
    try
    {
        var dbFactory = host.Services.GetRequiredService<IDbContextFactory<ClaraDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        console.MarkupLine("[dim]Database tables ensured.[/]");
    }
    catch (Exception ex)
    {
        console.MarkupLine($"[yellow]Database init warning: {ex.Message.EscapeMarkup()}[/]");
    }
}

// Initialize MCP servers
var mcpManager = host.Services.GetRequiredService<McpServerManager>();
console.MarkupLine("[dim]Initializing MCP servers...[/]");

try
{
    var mcpResults = await mcpManager.InitializeAsync();
    foreach (var (name, ok) in mcpResults)
    {
        var status = ok ? "[green]OK[/]" : "[red]FAIL[/]";
        console.MarkupLine($"  {status} {name.EscapeMarkup()}");
    }
}
catch (Exception ex)
{
    console.MarkupLine($"[yellow]MCP init warning: {ex.Message.EscapeMarkup()}[/]");
}

// Run REPL
var repl = host.Services.GetRequiredService<ChatRepl>();
try
{
    await repl.RunAsync();
}
finally
{
    await mcpManager.DisposeAsync();
}

return 0;
