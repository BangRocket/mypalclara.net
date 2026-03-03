using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Tools.Mcp;

/// <summary>
/// Manages the lifecycle of multiple MCP server connections.
/// Supports both stdio and HTTP transports.
/// </summary>
public class McpServerManager
{
    private readonly ConcurrentDictionary<string, IMcpClient> _clients = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpServerManager> _logger;

    public McpServerManager(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpServerManager>();
    }

    /// <summary>
    /// Start and connect to a stdio MCP server.
    /// </summary>
    public async Task StartServerAsync(
        string name,
        string command,
        string? args = null,
        Dictionary<string, string>? env = null,
        CancellationToken ct = default)
    {
        if (_clients.ContainsKey(name))
            throw new InvalidOperationException($"MCP server '{name}' is already running.");

        var client = new McpStdioClient(
            name, command, args, env,
            _loggerFactory.CreateLogger<McpStdioClient>());

        await client.ConnectAsync(ct);

        if (!_clients.TryAdd(name, client))
        {
            await client.DisposeAsync();
            throw new InvalidOperationException($"MCP server '{name}' was registered concurrently.");
        }

        _logger.LogInformation("MCP server '{Name}' started (stdio: {Command} {Args})", name, command, args ?? "");
    }

    /// <summary>
    /// Connect to an HTTP MCP server.
    /// </summary>
    public async Task StartHttpServerAsync(
        string name,
        string baseUrl,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        if (_clients.ContainsKey(name))
            throw new InvalidOperationException($"MCP server '{name}' is already running.");

        var client = new McpHttpClient(
            name, baseUrl, httpClient,
            _loggerFactory.CreateLogger<McpHttpClient>());

        await client.ConnectAsync(ct);

        if (!_clients.TryAdd(name, client))
        {
            await client.DisposeAsync();
            throw new InvalidOperationException($"MCP server '{name}' was registered concurrently.");
        }

        _logger.LogInformation("MCP server '{Name}' started (HTTP: {BaseUrl})", name, baseUrl);
    }

    /// <summary>
    /// Stop and disconnect an MCP server.
    /// </summary>
    public async Task StopServerAsync(string name, CancellationToken ct = default)
    {
        if (!_clients.TryRemove(name, out var client))
            throw new InvalidOperationException($"MCP server '{name}' is not running.");

        await client.DisposeAsync();
        _logger.LogInformation("MCP server '{Name}' stopped", name);
    }

    /// <summary>
    /// List tools available from a specific server.
    /// </summary>
    public async Task<IReadOnlyList<McpToolInfo>> GetToolsAsync(string serverName, CancellationToken ct = default)
    {
        if (!_clients.TryGetValue(serverName, out var client))
            throw new InvalidOperationException($"MCP server '{serverName}' is not running.");

        return await client.ListToolsAsync(ct);
    }

    /// <summary>
    /// Call a tool on a specific server.
    /// </summary>
    public async Task<string> CallToolAsync(string serverName, string toolName, string argsJson, CancellationToken ct = default)
    {
        if (!_clients.TryGetValue(serverName, out var client))
            throw new InvalidOperationException($"MCP server '{serverName}' is not running.");

        return await client.CallToolAsync(toolName, argsJson, ct);
    }

    /// <summary>
    /// Get list of all running server names.
    /// </summary>
    public IReadOnlyList<string> GetRunningServers() => _clients.Keys.ToList();

    /// <summary>
    /// Check if a server is running and connected.
    /// </summary>
    public bool IsRunning(string name) =>
        _clients.TryGetValue(name, out var client) && client.IsConnected;

    /// <summary>
    /// Get all tools from all running servers, prefixed with server name.
    /// </summary>
    public async Task<IReadOnlyList<(string ServerName, McpToolInfo Tool)>> GetAllToolsAsync(CancellationToken ct = default)
    {
        var result = new List<(string, McpToolInfo)>();

        foreach (var (name, client) in _clients)
        {
            try
            {
                var tools = await client.ListToolsAsync(ct);
                foreach (var tool in tools)
                    result.Add((name, tool));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list tools from MCP server '{Name}'", name);
            }
        }

        return result;
    }

    /// <summary>
    /// Stop all running servers.
    /// </summary>
    public async Task StopAllAsync()
    {
        foreach (var name in _clients.Keys.ToList())
        {
            try
            {
                await StopServerAsync(name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping MCP server '{Name}'", name);
            }
        }
    }
}
