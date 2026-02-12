using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyPalClara.Core.Modules;

/// <summary>
/// Interface for dynamically-loaded Gateway modules (e.g. Memory).
/// Discovered via assembly scanning at Gateway startup.
/// </summary>
public interface IGatewayModule
{
    string Name { get; }
    void ConfigureServices(IServiceCollection services, IConfiguration config);
    Task InitializeAsync(IServiceProvider services, CancellationToken ct);
    Task ShutdownAsync(CancellationToken ct);
}
