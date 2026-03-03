using Clara.Core;
using Clara.Core.Config;
using Clara.Core.Data;
using Clara.Core.Events;
using Clara.Core.Llm;
using Clara.Core.Tools;
using Clara.Core.Tools.ToolPolicy;
using Clara.Gateway.Hubs;
using Clara.Gateway.Hooks;
using Clara.Gateway.Pipeline;
using Clara.Gateway.Pipeline.Middleware;
using Clara.Gateway.Pipeline.Stages;
using Clara.Gateway.Queues;
using Clara.Gateway.Sandbox;
using Clara.Gateway.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// Core services (LLM, memory, prompt, sessions, events)
builder.Services.AddClaraCore(builder.Configuration);

// DbContext — PostgreSQL if connection string contains Host=, otherwise SQLite
var connectionString = builder.Configuration.GetConnectionString("Clara");
if (!string.IsNullOrEmpty(connectionString) && connectionString.Contains("Host="))
{
    builder.Services.AddDbContext<ClaraDbContext>(opts =>
        opts.UseNpgsql(connectionString, o => o.UseVector()));
}
else
{
    var dbPath = Path.Combine("data", "clara.db");
    Directory.CreateDirectory("data");
    builder.Services.AddDbContext<ClaraDbContext>(opts =>
        opts.UseSqlite($"Data Source={dbPath}"));
}

// SignalR
builder.Services.AddSignalR();

// Gateway queue system
builder.Services.AddSingleton<LaneQueueManager>();
builder.Services.AddSingleton<QueueMetrics>();
builder.Services.AddHostedService<LaneQueueWorker>();

// Hooks
builder.Services.AddSingleton<HookRegistry>();
builder.Services.AddSingleton<HookExecutor>();

// Scheduler
builder.Services.AddHostedService<SchedulerService>();

// Background services
builder.Services.AddHostedService<SessionCleanupService>();
builder.Services.AddHostedService<MemoryConsolidationService>();

// Heartbeat
builder.Services.Configure<HeartbeatOptions>(builder.Configuration.GetSection("Clara:Heartbeat"));
builder.Services.AddHostedService<HeartbeatService>();

// Sandbox
builder.Services.AddSingleton<ISandboxProvider, DockerSandbox>();
builder.Services.AddSingleton<SandboxManager>();

// Tool system
builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();
builder.Services.AddSingleton<ToolSelector>();
builder.Services.AddSingleton<ToolPolicyPipeline>();
builder.Services.AddScoped<LlmOrchestrator>();

// Pipeline middleware
builder.Services.AddScoped<IPipelineMiddleware, LoggingMiddleware>();
builder.Services.AddScoped<IPipelineMiddleware, StopPhraseMiddleware>();
builder.Services.AddScoped<IPipelineMiddleware, RateLimitMiddleware>();

// Pipeline stages
builder.Services.AddScoped<ContextBuildStage>();
builder.Services.AddScoped<ToolSelectionStage>();
builder.Services.AddScoped<LlmOrchestrationStage>();
builder.Services.AddScoped<ResponseRoutingStage>();

// Pipeline
builder.Services.AddScoped<IMessagePipeline, MessagePipeline>();

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    });

// CORS
builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetValue<string>("Clara:Gateway:CorsOrigins");
    options.AddDefaultPolicy(policy =>
    {
        if (!string.IsNullOrEmpty(origins))
        {
            policy.WithOrigins(origins.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// Ensure database exists
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Register built-in tools
app.Services.RegisterBuiltInTools();

// Load hooks
var hookRegistry = app.Services.GetRequiredService<HookRegistry>();
var hooksPath = builder.Configuration.GetValue("Hooks:Directory", "hooks/hooks.yaml");
if (hooksPath is not null)
    hookRegistry.LoadFromYaml(hooksPath);

app.UseCors();
app.MapControllers();
app.MapHub<AdapterHub>("/hubs/adapter");
app.MapHub<MonitorHub>("/hubs/monitor");

// Publish startup event
var eventBus = app.Services.GetRequiredService<IClaraEventBus>();
await eventBus.PublishAsync(new ClaraEvent(LifecycleEvents.Startup, DateTime.UtcNow));

Log.Information("Clara Gateway started");

app.Run();
