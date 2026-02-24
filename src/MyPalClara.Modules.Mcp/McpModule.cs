using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp;

public class McpModule : IGatewayModule
{
    public string Name => "mcp";
    public string Description => "MCP server lifecycle, tool discovery, and execution";

    private ModuleHealth _health = ModuleHealth.Stopped();
    private McpToolSource? _toolSource;

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<McpServerManager>();
        services.AddSingleton<IMcpServerManager>(sp => sp.GetRequiredService<McpServerManager>());
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        var manager = services.GetRequiredService<IMcpServerManager>();
        var registry = services.GetService<IToolRegistry>();

        _toolSource = new McpToolSource(manager);
        registry?.RegisterSource(_toolSource);

        _health = ModuleHealth.Running();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _health = ModuleHealth.Stopped();
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth() => _health;

    public void ConfigureEvents(IEventBus events, IGatewayBridge bridge) { }
}
