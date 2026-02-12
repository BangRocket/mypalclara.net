using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using MyPalClara.Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Load Clara config
var config = ConfigLoader.Bind(builder.Configuration);
builder.Services.AddSingleton(config);

// Create GatewayClient
var gatewayUri = new Uri($"ws://{config.Gateway.Host}:{config.Gateway.Port}/ws");
var gatewayLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<GatewayClient>();
var gateway = new GatewayClient(gatewayUri, config.Gateway.Secret, "discord", "discord-bot", gatewayLogger);
builder.Services.AddSingleton(gateway);

// Register services
builder.Services.AddSingleton<AttachmentHandler>();
builder.Services.AddSingleton<MessageHandler>();
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());

var host = builder.Build();

// Connect to Gateway before starting the host
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Connecting to Gateway at {Uri}...", gatewayUri);
await gateway.ConnectAsync();
logger.LogInformation("Gateway connected.");

// On shutdown, dispose GatewayClient
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    logger.LogInformation("Disposing Gateway client...");
    gateway.DisposeAsync().AsTask().GetAwaiter().GetResult();
});

await host.RunAsync();
