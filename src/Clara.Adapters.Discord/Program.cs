using Clara.Adapters.Discord;
using Clara.Core.Config;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using Serilog;

// Configuration
var botToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
if (string.IsNullOrEmpty(botToken))
{
    Console.Error.WriteLine("Error: DISCORD_BOT_TOKEN environment variable is required");
    return 1;
}

var gatewayUrl = Environment.GetEnvironmentVariable("CLARA_GATEWAY_URL") ?? "http://127.0.0.1:18789";
var gatewaySecret = Environment.GetEnvironmentVariable("CLARA_GATEWAY_SECRET");
var maxImages = int.TryParse(Environment.GetEnvironmentVariable("DISCORD_MAX_IMAGES"), out var mi) ? mi : 1;
var maxImageDim = int.TryParse(Environment.GetEnvironmentVariable("DISCORD_MAX_IMAGE_DIMENSION"), out var mid) ? mid : 1568;
var allowedServers = Environment.GetEnvironmentVariable("DISCORD_ALLOWED_SERVERS")
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(s => s.Trim())
    .ToList() ?? [];

// Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());

var options = new DiscordOptions
{
    BotToken = botToken,
    AllowedServers = allowedServers,
    MaxImages = maxImages,
    MaxImageDimension = maxImageDim,
};

// Build DSharpPlus client
var discordConfig = new DiscordConfiguration
{
    Token = botToken,
    TokenType = TokenType.Bot,
    Intents = DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents,
    LoggerFactory = loggerFactory,
};

var discordClient = new DiscordClient(discordConfig);

// Build components
var gatewayClient = new DiscordGatewayClient(
    gatewayUrl, gatewaySecret,
    loggerFactory.CreateLogger<DiscordGatewayClient>());

var responseSender = new DiscordResponseSender(
    discordClient,
    loggerFactory.CreateLogger<DiscordResponseSender>());

var slashCommands = new DiscordSlashCommands(
    loggerFactory.CreateLogger<DiscordSlashCommands>());

var imageHandler = new DiscordImageHandler(
    new HttpClient(),
    loggerFactory.CreateLogger<DiscordImageHandler>(),
    maxImageDim);

var adapter = new DiscordAdapter(
    discordClient,
    gatewayClient,
    responseSender,
    slashCommands,
    imageHandler,
    options,
    loggerFactory.CreateLogger<DiscordAdapter>());

// Handle shutdown
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await adapter.StartAsync(cts.Token);
    Log.Information("Discord adapter running. Press Ctrl+C to stop.");
    await Task.Delay(-1, cts.Token);
}
catch (OperationCanceledException)
{
    Log.Information("Shutting down...");
}
finally
{
    await adapter.DisposeAsync();
    await Log.CloseAndFlushAsync();
}

return 0;
