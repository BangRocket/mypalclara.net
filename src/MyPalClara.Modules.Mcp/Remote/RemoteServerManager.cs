using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Mcp.Models;

namespace MyPalClara.Modules.Mcp.Remote;

public class RemoteServerManager
{
    private readonly ConcurrentDictionary<string, RemoteServerConnection> _servers = new();
    private readonly HttpClient _httpClient;
    private readonly ILogger<RemoteServerManager> _logger;

    public RemoteServerManager(HttpClient httpClient, ILogger<RemoteServerManager> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task ConnectAsync(McpServerConfig config, CancellationToken ct = default)
    {
        var conn = new RemoteServerConnection(config, _httpClient, _logger);
        await conn.ConnectAsync(ct);
        _servers[config.Name] = conn;
    }

    public RemoteServerConnection? GetServer(string name) =>
        _servers.TryGetValue(name, out var s) ? s : null;
}
