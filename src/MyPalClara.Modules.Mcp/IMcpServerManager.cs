using System.Text.Json;
using MyPalClara.Modules.Mcp.Models;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp;

public interface IMcpServerManager
{
    IReadOnlyList<(string ServerName, McpTool Tool)> GetAllDiscoveredTools();
    Task<IReadOnlyList<McpServerConfig>> ListServersAsync(CancellationToken ct = default);
    Task<ServerStatus> GetStatusAsync(string serverName, CancellationToken ct = default);
    Task<ToolResult> CallToolAsync(string serverName, string toolName,
        Dictionary<string, JsonElement> args, CancellationToken ct = default);
    Task InstallAsync(string packageOrUrl, string? name = null, CancellationToken ct = default);
    Task UninstallAsync(string name, CancellationToken ct = default);
    Task EnableAsync(string name, CancellationToken ct = default);
    Task DisableAsync(string name, CancellationToken ct = default);
    Task RestartAsync(string name, CancellationToken ct = default);
    Task HotReloadAsync(string name, CancellationToken ct = default);
    Task RefreshToolsAsync(string name, CancellationToken ct = default);
    Task<string> OAuthStartAsync(string name, CancellationToken ct = default);
    Task OAuthCompleteAsync(string name, string code, CancellationToken ct = default);
    Task<string> OAuthStatusAsync(string name, CancellationToken ct = default);
}
