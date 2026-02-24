using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Graph;

public class GraphModule : IGatewayModule
{
    public string Name => "graph";
    public string Description => "FalkorDB entity/relationship graph memory";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
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
