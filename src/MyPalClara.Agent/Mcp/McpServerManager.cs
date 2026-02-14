using System.Text.Json;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Llm;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MyPalClara.Agent.Mcp;

/// <summary>
/// Manages MCP client instances for all configured servers.
/// Uses the official .NET MCP SDK (stdio transport).
/// Tool names are namespaced as {server}__{tool}.
/// </summary>
public sealed class McpServerManager : IAsyncDisposable
{
    private readonly ClaraConfig _config;
    private readonly McpConfigLoader _configLoader;
    private readonly ILogger<McpServerManager> _logger;

    // server name → (client, tools)
    private readonly Dictionary<string, (McpClient Client, IList<McpClientTool> Tools)> _servers = new();

    // namespaced tool name → (server name, original tool name)
    private readonly Dictionary<string, (string Server, string Tool)> _toolIndex = new();

    public McpServerManager(ClaraConfig config, McpConfigLoader configLoader, ILogger<McpServerManager> logger)
    {
        _config = config;
        _configLoader = configLoader;
        _logger = logger;
    }

    /// <summary>Initialize all enabled servers. Returns server name → success.</summary>
    public async Task<Dictionary<string, bool>> InitializeAsync(CancellationToken ct = default)
    {
        var results = new Dictionary<string, bool>();
        var configs = _configLoader.LoadLocalConfigs(_config.Mcp.ServersDir);

        foreach (var cfg in configs.Where(c => c.Enabled && c.AutoStart))
        {
            try
            {
                await StartServerAsync(cfg, ct);
                results[cfg.Name] = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start MCP server '{Name}'", cfg.Name);
                results[cfg.Name] = false;
            }
        }

        _logger.LogInformation("MCP initialized: {Ok}/{Total} servers started",
            results.Count(kv => kv.Value), results.Count);

        return results;
    }

    private async Task StartServerAsync(LocalServerConfig cfg, CancellationToken ct)
    {
        _logger.LogInformation("Starting MCP server '{Name}': {Cmd} {Args}",
            cfg.Name, cfg.Command, string.Join(" ", cfg.Args));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = cfg.Name,
            Command = cfg.Command,
            Arguments = cfg.Args,
            WorkingDirectory = cfg.Cwd,
            EnvironmentVariables = cfg.Env.Count > 0 ? cfg.Env.ToDictionary(kv => kv.Key, kv => (string?)kv.Value) : null,
        });

        var client = await McpClient.CreateAsync(
            transport,
            clientOptions: new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "Clara.NET", Version = "1.0.0" },
                InitializationTimeout = TimeSpan.FromSeconds(30),
            },
            cancellationToken: ct);

        var tools = await client.ListToolsAsync(cancellationToken: ct);
        _servers[cfg.Name] = (client, tools);

        // Build tool index with namespacing
        foreach (var tool in tools)
        {
            var namespacedName = $"{cfg.Name}__{tool.Name}";
            _toolIndex[namespacedName] = (cfg.Name, tool.Name);
        }

        _logger.LogInformation("MCP server '{Name}' started with {ToolCount} tools",
            cfg.Name, tools.Count);
    }

    /// <summary>Get all tools in Anthropic/Claude format for sending to LLM.</summary>
    public List<ToolSchema> GetAllToolSchemas()
    {
        var schemas = new List<ToolSchema>();

        foreach (var (serverName, (_, tools)) in _servers)
        {
            foreach (var tool in tools)
            {
                var namespacedName = $"{serverName}__{tool.Name}";
                var inputSchema = tool.JsonSchema.ValueKind != JsonValueKind.Undefined
                    ? tool.JsonSchema
                    : JsonDocument.Parse("{}").RootElement;

                schemas.Add(new ToolSchema(namespacedName, tool.Description ?? "", inputSchema));
            }
        }

        return schemas;
    }

    /// <summary>Parse a namespaced tool name "server__tool" into (server, tool).</summary>
    public (string? Server, string Tool) ParseToolName(string name)
    {
        if (_toolIndex.TryGetValue(name, out var entry))
            return entry;

        var idx = name.IndexOf("__", StringComparison.Ordinal);
        return idx > 0 ? (name[..idx], name[(idx + 2)..]) : (null, name);
    }

    /// <summary>Call a tool by its namespaced name and return the text result.</summary>
    public async Task<string> CallToolAsync(string namespacedName, JsonElement arguments, CancellationToken ct = default)
    {
        var (serverName, toolName) = ParseToolName(namespacedName);
        if (serverName is null || !_servers.TryGetValue(serverName, out var entry))
            return $"Error: Unknown tool or server for '{namespacedName}'";

        try
        {
            var args = new Dictionary<string, object?>();
            if (arguments.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in arguments.EnumerateObject())
                    args[prop.Name] = DeserializeValue(prop.Value);
            }

            var result = await entry.Client.CallToolAsync(toolName, args, cancellationToken: ct);

            // Concatenate all text content blocks
            var texts = result.Content
                .OfType<TextContentBlock>()
                .Select(c => c.Text);
            return string.Join("\n", texts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool call failed: {Tool}", namespacedName);
            return $"Error: {ex.Message}";
        }
    }

    private static object? DeserializeValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(DeserializeValue).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => DeserializeValue(p.Value)),
            _ => element.GetRawText(),
        };
    }

    /// <summary>Get status info for all servers.</summary>
    public IReadOnlyDictionary<string, int> GetServerStatus()
    {
        return _servers.ToDictionary(kv => kv.Key, kv => kv.Value.Tools.Count);
    }

    /// <summary>Get tools for a specific server.</summary>
    public IList<McpClientTool>? GetServerTools(string serverName)
    {
        return _servers.TryGetValue(serverName, out var entry) ? entry.Tools : null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (name, (client, _)) in _servers)
        {
            try
            {
                await client.DisposeAsync();
                _logger.LogDebug("Disposed MCP server '{Name}'", name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MCP server '{Name}'", name);
            }
        }
        _servers.Clear();
        _toolIndex.Clear();
    }
}
