using MyPalClara.Core.Chat;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Data;
using MyPalClara.Core.Identity;
using MyPalClara.Core.Llm;
using MyPalClara.Core.Memory;
using MyPalClara.Core.Orchestration;
using MyPalClara.Core.Personality;
using MyPalClara.Agent.Llm;
using MyPalClara.Agent.Mcp;
using RookProvider = MyPalClara.Core.Llm.RookProvider;
using MyPalClara.Agent.Modules;
using MyPalClara.Agent.Orchestration;
using MyPalClara.Skills;
using MyPalClara.Web.Components;
using MyPalClara.Web.Hubs;
using MyPalClara.Web.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// -- Load config --------------------------------------------------------
var config = ConfigLoader.Bind(builder.Configuration);
builder.Services.AddSingleton(config);

// -- Logging ------------------------------------------------------------
builder.Services.AddLogging(lb => lb
    .SetMinimumLevel(LogLevel.Information)
    .AddConsole());

// -- Database -----------------------------------------------------------
var connectionString = config.Database.Url;
builder.Services.AddPooledDbContextFactory<ClaraDbContext>(opts =>
    opts.UseNpgsql(connectionString));

// -- Core services ------------------------------------------------------
builder.Services.AddSingleton<PersonalityLoader>();
builder.Services.AddSingleton<UserIdentityService>();
builder.Services.AddSingleton<ChatHistoryService>();

// -- LLM ----------------------------------------------------------------
builder.Services.AddSingleton<LlmCallLogger>();

if (config.Llm.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHttpClient<ILlmProvider, AnthropicProvider>();
else
    builder.Services.AddHttpClient<ILlmProvider, OpenAiProvider>();

builder.Services.AddHttpClient<RookProvider>();

// -- MCP ----------------------------------------------------------------
builder.Services.AddSingleton<McpConfigLoader>();
builder.Services.AddSingleton<McpServerManager>();

// -- Orchestration ------------------------------------------------------
builder.Services.AddSingleton<ToolPolicyEvaluator>();
builder.Services.AddSingleton<ToolExecutor>();
builder.Services.AddSingleton<LlmOrchestrator>();

// -- Skills -------------------------------------------------------------
builder.Services.AddSingleton<SkillRegistry>();
builder.Services.AddSingleton(new SkillsSettings
{
    Directory = "~/.mypalclara/skills",
    Enabled = true,
});

// -- Blazor Server ------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// -- SignalR ------------------------------------------------------------
builder.Services.AddSignalR();

// -- Web chat service ---------------------------------------------------
builder.Services.AddScoped<WebChatService>();

// -- Module discovery ---------------------------------------------------
var loggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
var startupLogger = loggerFactory.CreateLogger("Web.Startup");

var pluginsDir = Path.IsPathRooted(config.Gateway.PluginsDir)
    ? config.Gateway.PluginsDir
    : Path.Combine(AppContext.BaseDirectory, config.Gateway.PluginsDir);
var modules = ModuleLoader.DiscoverAndConfigure(builder.Services, builder.Configuration, pluginsDir, startupLogger);

// If no memory module was loaded, register a null IMemoryService
if (!builder.Services.Any(sd => sd.ServiceType == typeof(IMemoryService)))
{
    builder.Services.AddSingleton<IMemoryService>(sp => null!);
}

// -- Build --------------------------------------------------------------
var app = builder.Build();

// -- Initialize modules -------------------------------------------------
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();
await ModuleLoader.InitializeAllAsync(modules, app.Services, appLogger);

// -- Initialize MCP servers ---------------------------------------------
var mcpManager = app.Services.GetRequiredService<McpServerManager>();
await mcpManager.InitializeAsync();

// -- Initialize Skills --------------------------------------------------
var skillRegistry = app.Services.GetRequiredService<SkillRegistry>();
var skillsSettings = app.Services.GetRequiredService<SkillsSettings>();
if (skillsSettings.Enabled)
{
    try
    {
        await skillRegistry.LoadSkillsAsync(skillsSettings.Directory);
    }
    catch (Exception ex)
    {
        appLogger.LogWarning(ex, "Failed to load skills from {Dir}", skillsSettings.Directory);
    }
}

// -- Middleware pipeline ------------------------------------------------
app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();

// -- Health check -------------------------------------------------------
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "1.0.0" }));

// -- SignalR hub --------------------------------------------------------
app.MapHub<DashboardHub>("/hubs/dashboard");

// -- REST API endpoints -------------------------------------------------
app.MapPost("/api/v1/chat", async (HttpContext httpContext, WebChatService chatService) =>
{
    var body = await httpContext.Request.ReadFromJsonAsync<ChatApiRequest>();
    if (body is null || string.IsNullOrWhiteSpace(body.Message))
    {
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsJsonAsync(new { error = "message is required" });
        return;
    }

    httpContext.Response.ContentType = "text/event-stream";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers.Connection = "keep-alive";

    var ct = httpContext.RequestAborted;

    await foreach (var evt in chatService.ChatAsync(body.Message, body.Tier, ct))
    {
        var data = evt switch
        {
            TextChunkEvent tc => new { type = "text", text = tc.Text },
            ToolStartEvent ts => (object)new { type = "tool_start", tool = ts.ToolName, step = ts.Step },
            ToolResultEvent tr => (object)new { type = "tool_result", tool = tr.ToolName, success = tr.Success, preview = tr.OutputPreview },
            CompleteEvent ce => (object)new { type = "complete", text = ce.FullText, toolCount = ce.ToolCount },
            _ => (object)new { type = "unknown" },
        };

        var json = JsonSerializer.Serialize(data);
        await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }
});

app.MapGet("/api/v1/memories", async (string? q, int? limit, IMemoryService? memory, ClaraConfig cfg, UserIdentityService identity) =>
{
    if (memory is null)
        return Results.Ok(new { error = "Memory service not available", items = Array.Empty<object>() });

    var query = q ?? "";
    var max = limit ?? 10;
    var userIds = await identity.ResolveAllUserIdsAsync(cfg.UserId);
    var items = await memory.SearchAsync(query, userIds, max);
    return Results.Ok(items);
});

app.MapGet("/api/v1/sessions", () =>
{
    // No Gateway reference — in-process chat only
    return Results.Ok(new { message = "In-process chat via WebChatService. No external adapter sessions.", sessions = Array.Empty<object>() });
});

app.MapGet("/api/v1/skills", (SkillRegistry registry) =>
{
    var skills = registry.GetAll().Select(s => new
    {
        s.Name,
        s.Description,
        s.Version,
        triggers = s.Triggers.Select(t => t.Pattern),
        toolsRequired = s.ToolsRequired,
    });
    return Results.Ok(skills);
});

app.MapGet("/api/v1/mcp/servers", (McpServerManager mcp) =>
{
    var status = mcp.GetServerStatus();
    return Results.Ok(status);
});

// -- Blazor -------------------------------------------------------------
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// -- Shutdown hook ------------------------------------------------------
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try { await ModuleLoader.ShutdownAllAsync(modules, appLogger); } catch { }
        try { await mcpManager.DisposeAsync(); } catch { }
    });
});

// -- Run ----------------------------------------------------------------
var port = config.Gateway.Port + 1; // Web runs on Gateway port + 1
var host = config.Gateway.Host ?? "0.0.0.0";
app.Urls.Add($"http://{host}:{port}");

appLogger.LogInformation("MyPalClara.Web starting on http://{Host}:{Port}", host, port);
await app.RunAsync();

// -- Request DTOs -------------------------------------------------------
record ChatApiRequest(string Message, string? Tier = null);
