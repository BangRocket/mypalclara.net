using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.Mcp;

/// <summary>Reads MCP server configs from the .mcp_servers directory.</summary>
public sealed class McpConfigLoader
{
    private readonly ILogger<McpConfigLoader> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public McpConfigLoader(ILogger<McpConfigLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>Load all local server configs from {serversDir}/local/{name}/config.json.</summary>
    public List<LocalServerConfig> LoadLocalConfigs(string serversDir)
    {
        var configs = new List<LocalServerConfig>();
        var localDir = Path.Combine(serversDir, "local");

        Directory.CreateDirectory(localDir);

        foreach (var dir in Directory.GetDirectories(localDir))
        {
            var configPath = Path.Combine(dir, "config.json");
            if (!File.Exists(configPath)) continue;

            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<LocalServerConfig>(json, JsonOpts);
                if (config is not null)
                {
                    if (string.IsNullOrEmpty(config.Name))
                        config.Name = Path.GetFileName(dir);
                    configs.Add(config);
                    _logger.LogDebug("Loaded MCP config: {Name} (enabled={Enabled})", config.Name, config.Enabled);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load MCP config from {Path}", configPath);
            }
        }

        _logger.LogInformation("Loaded {Count} local MCP server configs", configs.Count);
        return configs;
    }

    /// <summary>Load all remote server configs from {serversDir}/remote/{name}/config.json.</summary>
    public List<RemoteServerConfig> LoadRemoteConfigs(string serversDir)
    {
        var configs = new List<RemoteServerConfig>();
        var remoteDir = Path.Combine(serversDir, "remote");

        Directory.CreateDirectory(remoteDir);

        foreach (var dir in Directory.GetDirectories(remoteDir))
        {
            var configPath = Path.Combine(dir, "config.json");
            if (!File.Exists(configPath)) continue;

            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<RemoteServerConfig>(json, JsonOpts);
                if (config is not null)
                {
                    if (string.IsNullOrEmpty(config.Name))
                        config.Name = Path.GetFileName(dir);
                    configs.Add(config);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load remote MCP config from {Path}", configPath);
            }
        }

        return configs;
    }
}
