using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Email;

public class EmailModule : IGatewayModule
{
    public string Name => "email";
    public string Description => "Email account polling, rule evaluation, and alerts";

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
