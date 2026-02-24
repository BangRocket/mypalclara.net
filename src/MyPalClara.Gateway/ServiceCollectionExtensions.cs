using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Gateway.Events;
using MyPalClara.Gateway.Hooks;
using MyPalClara.Gateway.Modules;
using MyPalClara.Gateway.Scheduling;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all gateway services: EventBus, HookManager, Scheduler, ModuleLoader,
    /// ModuleRegistry, and GatewayBridge.
    /// </summary>
    public static IServiceCollection AddMyPalClaraGateway(this IServiceCollection services, IConfiguration config)
    {
        // Core event infrastructure
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<EventBus>());

        // Hooks
        services.AddSingleton<HookManager>();

        // Scheduler
        services.AddSingleton<Scheduler>();
        services.AddSingleton<IScheduler>(sp => sp.GetRequiredService<Scheduler>());
        services.AddHostedService(sp => sp.GetRequiredService<Scheduler>());

        // Module system
        services.AddSingleton<ModuleLoader>();
        services.AddSingleton<ModuleRegistry>();
        services.AddSingleton<GatewayBridge>();
        services.AddSingleton<IGatewayBridge>(sp => sp.GetRequiredService<GatewayBridge>());

        return services;
    }

    /// <summary>
    /// Loads hooks, scheduler tasks, discovers and starts modules, then publishes gateway:startup.
    /// Call after the host is built and services are available.
    /// </summary>
    public static async Task StartGatewayAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MyPalClara.Gateway.Startup");
        var config = services.GetRequiredService<IConfiguration>();

        // Load hooks from YAML
        var hookManager = services.GetRequiredService<HookManager>();
        var hooksDir = Environment.GetEnvironmentVariable("CLARA_HOOKS_DIR")
            ?? config.GetValue<string>("Hooks:Directory")
            ?? "./hooks";
        var hooksFile = Path.Combine(hooksDir, "hooks.yaml");
        hookManager.LoadFromFile(hooksFile);
        logger.LogInformation("Hooks loaded from {Path}", hooksFile);

        // Load scheduler tasks from YAML
        var scheduler = services.GetRequiredService<Scheduler>();
        var schedulerDir = Environment.GetEnvironmentVariable("CLARA_SCHEDULER_DIR")
            ?? config.GetValue<string>("Scheduler:Directory")
            ?? ".";
        var schedulerFile = Path.Combine(schedulerDir, "scheduler.yaml");
        scheduler.LoadFromFile(schedulerFile);
        logger.LogInformation("Scheduler tasks loaded from {Path}", schedulerFile);

        // Discover and start modules
        var loader = services.GetRequiredService<ModuleLoader>();
        var registry = services.GetRequiredService<ModuleRegistry>();
        var eventBus = services.GetRequiredService<IEventBus>();
        var bridge = services.GetRequiredService<IGatewayBridge>();

        var modulesDir = Environment.GetEnvironmentVariable("CLARA_MODULES_DIR")
            ?? config.GetValue<string>("Modules:Directory")
            ?? "./modules";
        var modules = loader.LoadFromDirectory(modulesDir);
        registry.RegisterModules(modules, config);
        await registry.StartAllAsync(services, eventBus, bridge, ct);
        logger.LogInformation("Started {Count} modules", modules.Count);

        // Publish startup event
        await eventBus.PublishAsync(GatewayEvent.Create(EventTypes.GatewayStartup));
        logger.LogInformation("Gateway startup complete");
    }

    /// <summary>
    /// Stops all modules and publishes gateway:shutdown.
    /// Call during graceful shutdown.
    /// </summary>
    public static async Task StopGatewayAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MyPalClara.Gateway.Shutdown");

        // Stop modules
        var registry = services.GetRequiredService<ModuleRegistry>();
        await registry.StopAllAsync(ct);

        // Publish shutdown event
        var eventBus = services.GetRequiredService<IEventBus>();
        await eventBus.PublishAsync(GatewayEvent.Create(EventTypes.GatewayShutdown));

        logger.LogInformation("Gateway shutdown complete");
    }
}
