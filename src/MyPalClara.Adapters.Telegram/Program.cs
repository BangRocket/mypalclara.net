using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using MyPalClara.Adapters.Telegram;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

var config = ConfigLoader.Bind(builder.Configuration);
builder.Services.AddSingleton(config);

if (string.IsNullOrEmpty(config.Telegram.Token))
{
    Console.Error.WriteLine("Telegram bot token not configured. Set Telegram__Token in appsettings.json or environment.");
    return 1;
}

// Gateway client
var gatewayUri = new Uri($"ws://{config.Gateway.Host}:{config.Gateway.Port}/ws");
var gatewayLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<GatewayClient>();
var gateway = new GatewayClient(gatewayUri, config.Gateway.Secret, "telegram", "telegram-bot", gatewayLogger);
builder.Services.AddSingleton(gateway);

// Telegram services
builder.Services.AddSingleton<TelegramMessageHandler>();
builder.Services.AddSingleton<TelegramBotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramBotService>());

var host = builder.Build();

// Connect to Gateway
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Connecting to Gateway at {Uri}...", gatewayUri);
await gateway.ConnectAsync();
logger.LogInformation("Gateway connected.");

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    gateway.DisposeAsync().AsTask().GetAwaiter().GetResult();
});

await host.RunAsync();
return 0;
