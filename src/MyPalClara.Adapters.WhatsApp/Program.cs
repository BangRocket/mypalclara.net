using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using MyPalClara.Adapters.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

var config = ConfigLoader.Bind(builder.Configuration);
builder.Services.AddSingleton(config);

if (string.IsNullOrEmpty(config.WhatsApp.PhoneNumberId) || string.IsNullOrEmpty(config.WhatsApp.AccessToken))
{
    Console.Error.WriteLine("WhatsApp not configured. Set WhatsApp__PhoneNumberId and WhatsApp__AccessToken.");
    return 1;
}

// Gateway client
var gatewayUri = new Uri($"ws://{config.Gateway.Host}:{config.Gateway.Port}/ws");
var gatewayLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<GatewayClient>();
var gateway = new GatewayClient(gatewayUri, config.Gateway.Secret, "whatsapp", "whatsapp-bot", gatewayLogger);
builder.Services.AddSingleton(gateway);

// WhatsApp services
builder.Services.AddHttpClient<WhatsAppMessageSender>();
builder.Services.AddSingleton<WhatsAppWebhookHandler>();

builder.WebHost.UseUrls($"http://0.0.0.0:{config.WhatsApp.WebhookPort}");

var app = builder.Build();

// Connect to Gateway
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Connecting to Gateway at {Uri}...", gatewayUri);
await gateway.ConnectAsync();
logger.LogInformation("Gateway connected.");

// Map webhook endpoints
var handler = app.Services.GetRequiredService<WhatsAppWebhookHandler>();
var webhookPath = config.WhatsApp.WebhookPath ?? "/webhook";

app.MapGet(webhookPath, (HttpContext ctx) => handler.HandleVerification(ctx));
app.MapPost(webhookPath, async (HttpContext ctx) => await handler.HandleIncomingAsync(ctx));

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    gateway.DisposeAsync().AsTask().GetAwaiter().GetResult();
});

await app.RunAsync();
return 0;
