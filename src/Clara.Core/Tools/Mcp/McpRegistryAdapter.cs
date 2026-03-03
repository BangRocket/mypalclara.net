using Microsoft.Extensions.Logging;

namespace Clara.Core.Tools.Mcp;

/// <summary>
/// Bridges MCP server tools into Clara's IToolRegistry.
/// Discovers tools from all running MCP servers and registers them with namespaced names.
/// </summary>
public class McpRegistryAdapter
{
    private readonly McpServerManager _serverManager;
    private readonly IToolRegistry _registry;
    private readonly ILogger<McpRegistryAdapter> _logger;

    public McpRegistryAdapter(
        McpServerManager serverManager,
        IToolRegistry registry,
        ILogger<McpRegistryAdapter>? logger = null)
    {
        _serverManager = serverManager;
        _registry = registry;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<McpRegistryAdapter>.Instance;
    }

    /// <summary>
    /// Discovers tools from all running MCP servers and registers them in the tool registry.
    /// Existing MCP tools are replaced on refresh.
    /// </summary>
    public async Task RefreshToolsAsync(CancellationToken ct = default)
    {
        var allTools = await _serverManager.GetAllToolsAsync(ct);

        var registered = 0;
        foreach (var (serverName, toolInfo) in allTools)
        {
            var wrapper = new McpToolWrapper(_serverManager, serverName, toolInfo);
            _registry.Register(wrapper);
            registered++;
            _logger.LogDebug("Registered MCP tool: {ToolName} (from server '{ServerName}')", wrapper.Name, serverName);
        }

        _logger.LogInformation("Refreshed MCP tools: {Count} tools from {Servers} servers",
            registered, _serverManager.GetRunningServers().Count);
    }

    /// <summary>
    /// Refresh tools from a single server (e.g., after a new server is started).
    /// </summary>
    public async Task RefreshServerToolsAsync(string serverName, CancellationToken ct = default)
    {
        var tools = await _serverManager.GetToolsAsync(serverName, ct);

        foreach (var toolInfo in tools)
        {
            var wrapper = new McpToolWrapper(_serverManager, serverName, toolInfo);
            _registry.Register(wrapper);
            _logger.LogDebug("Registered MCP tool: {ToolName}", wrapper.Name);
        }

        _logger.LogInformation("Registered {Count} tools from MCP server '{ServerName}'", tools.Count, serverName);
    }
}
