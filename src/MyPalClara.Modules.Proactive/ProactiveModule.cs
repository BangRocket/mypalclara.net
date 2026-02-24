using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Proactive.Delivery;
using MyPalClara.Modules.Proactive.Engine;
using MyPalClara.Modules.Proactive.Notes;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Proactive;

public class ProactiveModule : IGatewayModule
{
    public string Name => "proactive";
    public string Description => "ORS assessment and proactive outreach messaging";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<NoteManager>();
        services.AddSingleton<OutreachDelivery>();
        services.AddSingleton<OrsEngine>();
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
        // Subscribe to message:sent to track user activity for ORS
        events.Subscribe(EventTypes.MessageSent, async evt =>
        {
            // Update user activity timestamp (used by ORS cadence)
            await Task.CompletedTask;
        });
    }
}
