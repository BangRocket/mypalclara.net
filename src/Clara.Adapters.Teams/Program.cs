using Clara.Adapters.Teams;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text.Json;

// Configuration from environment
var appId = Environment.GetEnvironmentVariable("TEAMS_APP_ID");
var appPassword = Environment.GetEnvironmentVariable("TEAMS_APP_PASSWORD");
var botName = Environment.GetEnvironmentVariable("TEAMS_BOT_NAME") ?? "Clara";
var gatewayUrl = Environment.GetEnvironmentVariable("CLARA_GATEWAY_URL") ?? "http://127.0.0.1:18789";
var gatewaySecret = Environment.GetEnvironmentVariable("CLARA_GATEWAY_SECRET");
var listenPort = int.TryParse(Environment.GetEnvironmentVariable("TEAMS_PORT"), out var p) ? p : 3978;

// Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// Configure Kestrel to listen on the specified port
builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");

var loggerFactory = LoggerFactory.Create(b => b.AddSerilog());

var options = new TeamsOptions
{
    AppId = appId,
    AppPassword = appPassword,
    BotName = botName,
    GatewayUrl = gatewayUrl,
    GatewaySecret = gatewaySecret,
};

var gatewayClient = new TeamsGatewayClient(
    gatewayUrl, gatewaySecret,
    loggerFactory.CreateLogger<TeamsGatewayClient>());

var httpClient = new HttpClient();

var adapter = new TeamsAdapter(
    gatewayClient,
    httpClient,
    options,
    loggerFactory.CreateLogger<TeamsAdapter>());

builder.Services.AddSingleton(adapter);

var app = builder.Build();

// Bot Framework messaging endpoint
app.MapPost("/api/messages", async (HttpContext ctx) =>
{
    // Validate authorization
    var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
    if (!adapter.ValidateAuth(authHeader))
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    // Read and parse the activity
    using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
    var activity = doc.RootElement;

    await adapter.HandleActivityAsync(activity);

    ctx.Response.StatusCode = 200;
});

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", adapter = "teams" }));

// Connect to gateway
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await adapter.StartAsync(cts.Token);
    Log.Information("Teams adapter listening on port {Port}. POST /api/messages to receive activities.", listenPort);
    await app.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Log.Information("Shutting down...");
}
finally
{
    await gatewayClient.DisposeAsync();
    await Log.CloseAndFlushAsync();
}
