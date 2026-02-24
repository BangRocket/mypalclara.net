using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyPalClara.Modules.Sdk;

public interface IGatewayModule
{
    string Name { get; }
    string Description { get; }
    void ConfigureServices(IServiceCollection services, IConfiguration config);
    Task StartAsync(IServiceProvider services, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    ModuleHealth GetHealth();
    void ConfigureEvents(IEventBus events, IGatewayBridge bridge);
}
