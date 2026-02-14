using MyPalClara.Adapters.Cli;
using MyPalClara.Adapters.Cli.Repl;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using MyPalClara.Core.Voice;
using MyPalClara.Voice;
using MyPalClara.Voice.Transcription;
using MyPalClara.Voice.Synthesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

var console = AnsiConsole.Console;

// Build host with DI — explicit config layering
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();
if (args.Length > 0)
    builder.Configuration.AddCommandLine(args);

// Bind ClaraConfig
ClaraConfig config;
try
{
    config = ConfigLoader.Bind(builder.Configuration);
}
catch (Exception ex)
{
    console.MarkupLine($"[red]Failed to load config: {ex.Message.EscapeMarkup()}[/]");
    return 1;
}

Banner.Print(console);
console.MarkupLine($"[dim]Provider: {config.Llm.Provider.EscapeMarkup()}, Model: {config.Llm.ActiveProvider.Model.EscapeMarkup()}[/]");

builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);

// --- Core singletons ---
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<IAnsiConsole>(console);

// --- Gateway client ---
var gatewayUrl = $"ws://{config.Gateway.Host ?? "localhost"}:{config.Gateway.Port}/ws";
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<GatewayClient>>();
    return new GatewayClient(
        new Uri(gatewayUrl),
        config.Gateway.Secret ?? "",
        "cli",
        $"cli-{Environment.MachineName}",
        logger);
});

// --- Voice ---
builder.Services.AddHttpClient<WhisperTranscriber>();
builder.Services.AddSingleton<ITranscriber>(sp => sp.GetRequiredService<WhisperTranscriber>());
builder.Services.AddHttpClient<ReplicateTtsSynthesizer>();
builder.Services.AddSingleton<ITtsSynthesizer>(sp => sp.GetRequiredService<ReplicateTtsSynthesizer>());
builder.Services.AddSingleton<VoiceManager>();

// --- REPL ---
builder.Services.AddSingleton<StreamingRenderer>();
builder.Services.AddSingleton<CommandDispatcher>();
builder.Services.AddSingleton<ChatRepl>();

var host = builder.Build();

// Connect to Gateway
var gateway = host.Services.GetRequiredService<GatewayClient>();
console.MarkupLine($"[dim]Connecting to Gateway at {gatewayUrl.EscapeMarkup()}...[/]");

try
{
    await gateway.ConnectAsync();
    console.MarkupLine("[green]Connected to Gateway.[/]");
}
catch (Exception ex)
{
    console.MarkupLine($"[red]Failed to connect to Gateway: {ex.Message.EscapeMarkup()}[/]");
    console.MarkupLine("[dim]Make sure the Gateway is running (dotnet run --project src/MyPalClara.Gateway)[/]");
    return 1;
}

// Run REPL
var repl = host.Services.GetRequiredService<ChatRepl>();
try
{
    await repl.RunAsync();
}
finally
{
    await gateway.DisposeAsync();
}

return 0;
