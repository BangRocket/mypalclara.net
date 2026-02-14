using MyPalClara.Core.Chat;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Data;
using MyPalClara.Core.Identity;
using MyPalClara.Core.Llm;
using MyPalClara.Core.Memory;
using MyPalClara.Core.Personality;
using MyPalClara.Agent.Llm;
using MyPalClara.Agent.Mcp;
using RookProvider = MyPalClara.Core.Llm.RookProvider;
using MyPalClara.Agent.Modules;
using MyPalClara.Agent.Orchestration;
using MyPalClara.Gateway.WebSocket;
using Microsoft.EntityFrameworkCore;
using System.Net.WebSockets;

var builder = WebApplication.CreateBuilder(args);

// ── Load config ─────────────────────────────────────────────────
var config = ConfigLoader.Bind(builder.Configuration);
builder.Services.AddSingleton(config);

// ── Logging ─────────────────────────────────────────────────────
builder.Services.AddLogging(lb => lb
    .SetMinimumLevel(LogLevel.Information)
    .AddConsole());

// ── Database ────────────────────────────────────────────────────
var connectionString = config.Database.Url;
builder.Services.AddPooledDbContextFactory<ClaraDbContext>(opts =>
    opts.UseNpgsql(connectionString));

// ── Core services ───────────────────────────────────────────────
builder.Services.AddSingleton<PersonalityLoader>();
builder.Services.AddSingleton<UserIdentityService>();
builder.Services.AddSingleton<ChatHistoryService>();

// ── LLM ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<LlmCallLogger>();

if (config.Llm.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHttpClient<ILlmProvider, AnthropicProvider>();
else
    builder.Services.AddHttpClient<ILlmProvider, OpenAiProvider>();

builder.Services.AddHttpClient<RookProvider>();

// ── MCP ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<McpConfigLoader>();
builder.Services.AddSingleton<McpServerManager>();

// ── Orchestration ───────────────────────────────────────────────
builder.Services.AddSingleton<ToolPolicyEvaluator>();
builder.Services.AddSingleton<ToolExecutor>();
builder.Services.AddSingleton<LlmOrchestrator>();

// ── Multi-agent routing ─────────────────────────────────────────
builder.Services.AddSingleton<MyPalClara.Agent.Llm.LlmProviderFactory>();
builder.Services.AddSingleton<MyPalClara.Gateway.Routing.AgentRouter>();

// ── Session compaction ──────────────────────────────────────────
builder.Services.AddSingleton<MyPalClara.Gateway.Sessions.SessionCompactor>();

// ── Scheduling ─────────────────────────────────────────────────
builder.Services.AddSingleton<MyPalClara.Gateway.Scheduling.IScheduledJob, MyPalClara.Gateway.Scheduling.MemoryCleanupJob>();
builder.Services.AddSingleton<MyPalClara.Gateway.Scheduling.IScheduledJob, MyPalClara.Gateway.Scheduling.ConversationArchivalJob>();
builder.Services.AddSingleton<MyPalClara.Gateway.Scheduling.IScheduledJob, MyPalClara.Gateway.Scheduling.HealthCheckJob>();
builder.Services.AddHostedService<MyPalClara.Gateway.Scheduling.CronService>();

// ── WebSocket ───────────────────────────────────────────────────
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<MessageRouter>();

// ── Module discovery ────────────────────────────────────────────
var loggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
var startupLogger = loggerFactory.CreateLogger("Gateway.Startup");

// Resolve plugins dir relative to the executable (where MSBuild copies module DLLs)
var pluginsDir = Path.IsPathRooted(config.Gateway.PluginsDir)
    ? config.Gateway.PluginsDir
    : Path.Combine(AppContext.BaseDirectory, config.Gateway.PluginsDir);
var modules = ModuleLoader.DiscoverAndConfigure(builder.Services, builder.Configuration, pluginsDir, startupLogger);

// If no memory module was loaded, register a null IMemoryService
if (!builder.Services.Any(sd => sd.ServiceType == typeof(IMemoryService)))
{
    builder.Services.AddSingleton<IMemoryService>(sp => null!);
}

// ── Build ───────────────────────────────────────────────────────
var app = builder.Build();

// ── Initialize modules ──────────────────────────────────────────
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();
await ModuleLoader.InitializeAllAsync(modules, app.Services, appLogger);

// ── Initialize MCP servers ──────────────────────────────────────
var mcpManager = app.Services.GetRequiredService<McpServerManager>();
await mcpManager.InitializeAsync();

// ── WebSocket middleware ────────────────────────────────────────
app.UseWebSockets();

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    var connectionId = Guid.NewGuid().ToString("N");
    var router = context.RequestServices.GetRequiredService<MessageRouter>();

    await router.HandleConnectionAsync(ws, connectionId, context.RequestAborted);
});

// ── Health check ────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "1.0.0" }));

// ── Shutdown hook ───────────────────────────────────────────────
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    _ = Task.Run(async () =>
    {
        await ModuleLoader.ShutdownAllAsync(modules, appLogger);
        await mcpManager.DisposeAsync();
    });
});

// ── Run ─────────────────────────────────────────────────────────
var port = config.Gateway.Port;
var host = config.Gateway.Host ?? "0.0.0.0";
app.Urls.Add($"http://{host}:{port}");

appLogger.LogInformation("Gateway starting on http://{Host}:{Port}", host, port);
await app.RunAsync();
