using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Games;

public class GamesModule : IGatewayModule
{
    public string Name => "games";
    public string Description => "AI move decisions for turn-based games";

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
