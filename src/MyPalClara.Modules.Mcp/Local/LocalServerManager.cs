using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Mcp.Models;

namespace MyPalClara.Modules.Mcp.Local;

public class LocalServerManager
{
    private readonly ConcurrentDictionary<string, LocalServerProcess> _servers = new();
    private readonly ILogger<LocalServerManager> _logger;

    public LocalServerManager(ILogger<LocalServerManager> logger) => _logger = logger;

    public async Task StartServerAsync(McpServerConfig config, CancellationToken ct = default)
    {
        var process = new LocalServerProcess(config, _logger);
        await process.StartAsync(ct);
        _servers[config.Name] = process;
    }

    public LocalServerProcess? GetServer(string name) =>
        _servers.TryGetValue(name, out var s) ? s : null;

    public IReadOnlyList<string> GetServerNames() => _servers.Keys.ToList();
}
