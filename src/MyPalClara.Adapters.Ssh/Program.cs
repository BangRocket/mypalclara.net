using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using MyPalClara.Adapters.Ssh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();
if (args.Length > 0)
    builder.Configuration.AddCommandLine(args);

ClaraConfig config;
try
{
    config = ConfigLoader.Bind(builder.Configuration);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load config: {ex.Message}");
    return 1;
}

builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);

builder.Services.AddSingleton(config);

// Factory that creates a new GatewayClient per SSH session
var gatewayUrl = $"ws://{config.Gateway.Host ?? "localhost"}:{config.Gateway.Port}/ws";
builder.Services.AddSingleton<Func<GatewayClient>>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return () => new GatewayClient(
        new Uri(gatewayUrl),
        config.Gateway.Secret ?? "",
        "ssh",
        $"ssh-{Guid.NewGuid():N}",
        loggerFactory.CreateLogger<GatewayClient>());
});

builder.Services.AddSingleton<SshServerHost>();

var host = builder.Build();

var sshServer = host.Services.GetRequiredService<SshServerHost>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

sshServer.Start();
logger.LogInformation("SSH adapter started. Gateway: {Url}", gatewayUrl);

// Block until the host shuts down (Ctrl+C)
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
var shutdownTcs = new TaskCompletionSource();
lifetime.ApplicationStopping.Register(() =>
{
    sshServer.Stop();
    shutdownTcs.TrySetResult();
});

await host.RunAsync();

return 0;
