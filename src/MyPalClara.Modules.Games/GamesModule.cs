using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Games;

public class GamesModule : IGatewayModule
{
    public string Name => "games";
    public string Description => "AI move decisions for turn-based games";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient("GamesRails");
        services.AddSingleton<GameReasoner>();
        services.AddSingleton<IGameReasoner>(sp => sp.GetRequiredService<GameReasoner>());
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        var reasoner = services.GetRequiredService<IGameReasoner>();
        var registry = services.GetService<IToolRegistry>();
        registry?.RegisterSource(new GameToolSource(reasoner));

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
