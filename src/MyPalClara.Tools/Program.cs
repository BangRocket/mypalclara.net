using MyPalClara.Core.Chat;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Data;
using MyPalClara.Core.Identity;
using MyPalClara.Core.Llm;
using MyPalClara.Core.Memory;
using MyPalClara.Gateway.Llm;
using MyPalClara.Memory;
using MyPalClara.Memory.Cache;
using MyPalClara.Memory.Context;
using MyPalClara.Memory.Dynamics;
using MyPalClara.Memory.Extraction;
using MyPalClara.Tools.Backfill;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Parse CLI args before host builder (host builder consumes them too)
var options = ParseArgs(args);
if (options is null) return 1;

// Build host with DI — same config layering as Clara.Cli
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

ClaraConfig config;
try
{
    config = ConfigLoader.Bind(builder.Configuration);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load config: {ex.Message}");
    return 1;
}

builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);

// --- Core singletons ---
builder.Services.AddSingleton(config);

// --- LLM providers (for fact extraction / topic extraction) ---
builder.Services.AddHttpClient<AnthropicProvider>();
builder.Services.AddSingleton<ILlmProvider>(sp => sp.GetRequiredService<AnthropicProvider>());
builder.Services.AddHttpClient<RookProvider>();

// --- Embedding client ---
builder.Services.AddHttpClient<EmbeddingClient>();

// --- Semantic memory store (FalkorDB) ---
builder.Services.AddSingleton<ISemanticMemoryStore, FalkorDbSemanticStore>();

// --- EF Core ---
builder.Services.AddDbContextFactory<ClaraDbContext>(opts =>
    opts.UseNpgsql(config.Database.Url));
builder.Services.AddSingleton<UserIdentityService>();
builder.Services.AddSingleton<ChatHistoryService>();

// --- FSRS / memory dynamics ---
builder.Services.AddSingleton<MemoryDynamicsService>();
builder.Services.AddSingleton<CompositeScorer>();

// --- Memory extraction ---
builder.Services.AddSingleton<ContradictionDetector>();
builder.Services.AddSingleton<FactExtractor>();
builder.Services.AddSingleton<SmartIngest>();

// --- Emotional context & topic recurrence ---
builder.Services.AddSingleton<EmotionalContext>();
builder.Services.AddSingleton<TopicRecurrence>();

// --- Redis cache (optional) ---
if (!string.IsNullOrEmpty(config.Memory.RedisUrl))
{
    builder.Services.AddStackExchangeRedisCache(opts =>
    {
        opts.Configuration = config.Memory.RedisUrl;
        opts.InstanceName = "clara:";
    });
}
builder.Services.AddSingleton<MemoryCache>();

// --- Memory service ---
builder.Services.AddSingleton<MemoryService>();

// --- LLM call logger ---
builder.Services.AddSingleton<LlmCallLogger>();

// --- Backfill runner ---
builder.Services.AddSingleton<BackfillRunner>();

var host = builder.Build();

// Ensure DB tables exist
if (!string.IsNullOrEmpty(config.Database.Url))
{
    try
    {
        var dbFactory = host.Services.GetRequiredService<IDbContextFactory<ClaraDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        Console.WriteLine("Database tables ensured.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Database init warning: {ex.Message}");
    }
}

// Ensure FalkorDB schema
if (!options.SkipMemory)
{
    try
    {
        var semanticStore = host.Services.GetRequiredService<ISemanticMemoryStore>();
        await semanticStore.EnsureSchemaAsync();
        Console.WriteLine("FalkorDB schema ensured.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FalkorDB schema warning: {ex.Message}");
    }
}

// Run backfill
var runner = host.Services.GetRequiredService<BackfillRunner>();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\nCancelling... (will save checkpoint)");
};

await runner.RunAsync(options, cts.Token);
return 0;

// --- CLI arg parsing ---
static BackfillOptions? ParseArgs(string[] args)
{
    var chatsDir = "chats";
    string? userId = null;
    var delayMs = 100;
    var concurrency = 3;
    var dryRun = false;
    string? source = null;
    var skipMemory = false;
    var skipHistory = false;
    var resetCheckpoint = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "backfill":
                break; // just the subcommand name
            case "--chats-dir" when i + 1 < args.Length:
                chatsDir = args[++i];
                break;
            case "--user-id" when i + 1 < args.Length:
                userId = args[++i];
                break;
            case "--delay" when i + 1 < args.Length:
                delayMs = int.Parse(args[++i]);
                break;
            case "--concurrency" when i + 1 < args.Length:
                concurrency = int.Parse(args[++i]);
                break;
            case "--dry-run":
                dryRun = true;
                break;
            case "--source" when i + 1 < args.Length:
                source = args[++i];
                break;
            case "--skip-memory":
                skipMemory = true;
                break;
            case "--skip-history":
                skipHistory = true;
                break;
            case "--reset-checkpoint":
                resetCheckpoint = true;
                break;
            case "--help" or "-h":
                PrintUsage();
                return null;
        }
    }

    return new BackfillOptions
    {
        ChatsDir = chatsDir,
        UserId = userId,
        DelayMs = delayMs,
        Concurrency = concurrency,
        DryRun = dryRun,
        Source = source,
        SkipMemory = skipMemory,
        SkipHistory = skipHistory,
        ResetCheckpoint = resetCheckpoint,
    };
}

static void PrintUsage()
{
    Console.WriteLine("""
        MyPalClara.Tools — Chat History Backfill

        Usage:
          dotnet run --project src/MyPalClara.Tools -- backfill [options]

        Options:
          --chats-dir <path>       Directory containing chat exports (default: chats)
          --user-id <id>           User ID to associate memories with (default: from config)
          --delay <ms>             Delay between exchanges in ms (default: 100)
          --concurrency <n>        Max concurrent LLM calls (default: 3)
          --dry-run                Parse and report stats without writing anything
          --source <name>          Process only: chatgpt, discord-dm, discord-server
          --skip-memory            Store chat history only, skip memory/graph/topic processing
          --skip-history           Skip chat history storage, only build memories
          --reset-checkpoint       Ignore checkpoint file and reprocess everything
          -h, --help               Show this help
        """);
}
