using Clara.Adapters.Cli;
using Microsoft.Extensions.Logging;
using Spectre.Console;

AnsiConsole.Write(new FigletText("Clara").Color(Color.Blue));
AnsiConsole.MarkupLine("[dim]Connecting to gateway...[/]");

var gatewayUrl = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("CLARA_GATEWAY_URL") ?? "http://localhost:18789";
var gatewaySecret = Environment.GetEnvironmentVariable("CLARA_GATEWAY_SECRET");
var userId = Environment.GetEnvironmentVariable("CLARA_CLI_USER") ?? Environment.UserName;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Warning);
});

var client = new CliGatewayClient(gatewayUrl, gatewaySecret, loggerFactory.CreateLogger<CliGatewayClient>());
var renderer = new CliRenderer();
var adapter = new CliAdapter(client, renderer, userId);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await adapter.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // Clean shutdown
}
finally
{
    await client.DisposeAsync();
    AnsiConsole.MarkupLine("[dim]Goodbye![/]");
}
