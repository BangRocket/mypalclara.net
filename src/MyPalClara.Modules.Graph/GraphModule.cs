using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Graph.Api;
using MyPalClara.Modules.Graph.Cache;
using MyPalClara.Modules.Graph.Client;
using MyPalClara.Modules.Graph.Extraction;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Graph;

public class GraphModule : IGatewayModule
{
    public string Name => "graph";
    public string Description => "FalkorDB entity/relationship graph memory";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<FalkorDbClient>();
        services.AddSingleton<GraphOperations>();
        services.AddSingleton<GraphCache>();
        services.AddSingleton<TripleExtractor>();
        services.AddSingleton<GraphApiService>();
    }

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

    public void ConfigureEvents(IEventBus events, IGatewayBridge bridge)
    {
        // Subscribe to message:sent for triple extraction
        events.Subscribe(EventTypes.MessageSent, async evt =>
        {
            // Fire-and-forget triple extraction
        });
    }
}
