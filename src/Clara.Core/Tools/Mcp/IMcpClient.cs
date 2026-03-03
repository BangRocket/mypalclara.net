namespace Clara.Core.Tools.Mcp;

/// <summary>
/// Interface for MCP (Model Context Protocol) client connections.
/// Supports JSON-RPC communication with MCP servers over stdio or HTTP.
/// </summary>
public interface IMcpClient : IAsyncDisposable
{
    string ServerName { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct = default);
    Task<string> CallToolAsync(string toolName, string argumentsJson, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
}

/// <summary>
/// Describes a tool exposed by an MCP server.
/// </summary>
public record McpToolInfo(string Name, string Description, string ParameterSchemaJson);
