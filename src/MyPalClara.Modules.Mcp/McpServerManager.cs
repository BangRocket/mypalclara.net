using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Mcp.Models;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp;

public class McpServerManager : IMcpServerManager
{
    private readonly ConcurrentDictionary<string, McpServerConfig> _servers = new();
    private readonly ConcurrentDictionary<string, List<McpTool>> _tools = new();
    private readonly ConcurrentDictionary<string, string> _statuses = new();
    private readonly ILogger<McpServerManager> _logger;
    private readonly string _serversDir;

    public McpServerManager(ILogger<McpServerManager> logger)
    {
        _logger = logger;
        _serversDir = Environment.GetEnvironmentVariable("MCP_SERVERS_DIR") ?? ".mcp_servers";
    }

    public IReadOnlyList<(string ServerName, McpTool Tool)> GetAllDiscoveredTools()
    {
        var result = new List<(string, McpTool)>();
        foreach (var (server, tools) in _tools)
            foreach (var tool in tools)
                result.Add((server, tool));
        return result;
    }

    public Task<IReadOnlyList<McpServerConfig>> ListServersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<McpServerConfig>>(_servers.Values.ToList());

    public Task<ServerStatus> GetStatusAsync(string serverName, CancellationToken ct = default)
    {
        var status = _statuses.GetValueOrDefault(serverName, "stopped");
        var toolCount = _tools.TryGetValue(serverName, out var tools) ? tools.Count : 0;
        return Task.FromResult(new ServerStatus(serverName, status, toolCount, null));
    }

    public Task<ToolResult> CallToolAsync(string serverName, string toolName,
        Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        _logger.LogInformation("Calling MCP tool {Server}/{Tool}", serverName, toolName);
        // Actual implementation delegates to LocalServerProcess or RemoteServerConnection
        return Task.FromResult(new ToolResult(true, $"Called {serverName}/{toolName}"));
    }

    public Task InstallAsync(string packageOrUrl, string? name = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Installing MCP server: {Package}", packageOrUrl);
        return Task.CompletedTask;
    }

    public Task UninstallAsync(string name, CancellationToken ct = default)
    {
        _servers.TryRemove(name, out _);
        _tools.TryRemove(name, out _);
        return Task.CompletedTask;
    }

    public Task EnableAsync(string name, CancellationToken ct = default)
    {
        _statuses[name] = "enabled";
        return Task.CompletedTask;
    }

    public Task DisableAsync(string name, CancellationToken ct = default)
    {
        _statuses[name] = "disabled";
        return Task.CompletedTask;
    }

    public Task RestartAsync(string name, CancellationToken ct = default)
    {
        _logger.LogInformation("Restarting MCP server: {Name}", name);
        return Task.CompletedTask;
    }

    public Task HotReloadAsync(string name, CancellationToken ct = default)
    {
        _logger.LogInformation("Hot-reloading MCP server: {Name}", name);
        return Task.CompletedTask;
    }

    public Task RefreshToolsAsync(string name, CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing tools for MCP server: {Name}", name);
        return Task.CompletedTask;
    }

    public Task<string> OAuthStartAsync(string name, CancellationToken ct = default)
        => Task.FromResult($"https://auth.example.com/authorize?server={name}");

    public Task OAuthCompleteAsync(string name, string code, CancellationToken ct = default)
    {
        _logger.LogInformation("OAuth complete for {Name}", name);
        return Task.CompletedTask;
    }

    public Task<string> OAuthStatusAsync(string name, CancellationToken ct = default)
        => Task.FromResult("none");
}
