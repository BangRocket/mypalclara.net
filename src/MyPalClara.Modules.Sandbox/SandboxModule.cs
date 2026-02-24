using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Sandbox;

public class SandboxModule : IGatewayModule
{
    public string Name => "sandbox";
    public string Description => "Docker/Incus code execution sandbox";

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
