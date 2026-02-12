using System.Reflection;
using MyPalClara.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.Modules;

/// <summary>
/// Discovers and loads IGatewayModule implementations from plugin assemblies.
/// Scans the plugins directory for .dll files at startup.
/// </summary>
public static class ModuleLoader
{
    /// <summary>
    /// Scan the plugins directory, find IGatewayModule implementations, and call ConfigureServices.
    /// Returns the discovered modules for later InitializeAsync/ShutdownAsync calls.
    /// </summary>
    public static List<IGatewayModule> DiscoverAndConfigure(
        IServiceCollection services,
        IConfiguration config,
        string pluginsDir,
        ILogger logger)
    {
        var modules = new List<IGatewayModule>();

        Directory.CreateDirectory(pluginsDir);

        foreach (var dll in Directory.GetFiles(pluginsDir, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var moduleTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(IGatewayModule).IsAssignableFrom(t));

                foreach (var moduleType in moduleTypes)
                {
                    var module = (IGatewayModule)Activator.CreateInstance(moduleType)!;
                    module.ConfigureServices(services, config);
                    modules.Add(module);
                    logger.LogInformation("Discovered module '{Name}' from {Assembly}",
                        module.Name, Path.GetFileName(dll));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load plugin assembly: {Dll}", Path.GetFileName(dll));
            }
        }

        logger.LogInformation("Module discovery complete: {Count} module(s) loaded", modules.Count);
        return modules;
    }

    /// <summary>Initialize all discovered modules.</summary>
    public static async Task InitializeAllAsync(
        IReadOnlyList<IGatewayModule> modules,
        IServiceProvider services,
        ILogger logger,
        CancellationToken ct = default)
    {
        foreach (var module in modules)
        {
            try
            {
                await module.InitializeAsync(services, ct);
                logger.LogInformation("Module '{Name}' initialized", module.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize module '{Name}'", module.Name);
            }
        }
    }

    /// <summary>Shutdown all modules gracefully.</summary>
    public static async Task ShutdownAllAsync(
        IReadOnlyList<IGatewayModule> modules,
        ILogger logger,
        CancellationToken ct = default)
    {
        foreach (var module in modules)
        {
            try
            {
                await module.ShutdownAsync(ct);
                logger.LogDebug("Module '{Name}' shut down", module.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error shutting down module '{Name}'", module.Name);
            }
        }
    }
}
