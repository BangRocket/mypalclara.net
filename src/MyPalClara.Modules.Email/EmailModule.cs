using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Modules.Email.Monitoring;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Email;

public class EmailModule : IGatewayModule
{
    public string Name => "email";
    public string Description => "Email account polling, rule evaluation, and alerts";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<EmailPoller>();
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        var poller = services.GetRequiredService<EmailPoller>();
        var registry = services.GetService<IToolRegistry>();
        registry?.RegisterSource(new EmailToolSource(poller));

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
