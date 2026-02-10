using System.Text.Json.Serialization;

namespace Clara.Core.Mcp;

/// <summary>Config for a local MCP server (stdio transport). Maps to .mcp_servers/local/{name}/config.json.</summary>
public sealed class LocalServerConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("auto_start")]
    public bool AutoStart { get; set; } = true;

    [JsonPropertyName("tool_count")]
    public int ToolCount { get; set; }

    [JsonPropertyName("tools")]
    public List<McpToolInfo> Tools { get; set; } = [];
}

/// <summary>Config for a remote MCP server (HTTP transport).</summary>
public sealed class RemoteServerConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    public McpServersBlock? McpServers { get; set; }

    [JsonPropertyName("_metadata")]
    public RemoteMetadata? Metadata { get; set; }
}

public sealed class McpServersBlock : Dictionary<string, McpServerEntry>;

public sealed class McpServerEntry
{
    [JsonPropertyName("serverUrl")]
    public string ServerUrl { get; set; } = "";

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = [];
}

public sealed class RemoteMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("transport")]
    public string Transport { get; set; } = "streamable-http";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

/// <summary>Tool info cached in config.json.</summary>
public sealed class McpToolInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("inputSchema")]
    public object? InputSchema { get; set; }
}
