using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using MyPalClara.App.Terminal;

var config = ConfigLoader.Bind(
    new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build());

var gatewayUri = new Uri($"ws://{config.Gateway.Host}:{config.Gateway.Port}/ws");
var logger = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning)).CreateLogger<GatewayClient>();
var gateway = new GatewayClient(gatewayUri, config.Gateway.Secret, "terminal", "terminal-tui", logger);

var app = new TerminalChatApp(gateway, config);
await app.RunAsync();
