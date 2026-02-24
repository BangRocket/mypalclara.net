using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Mcp.Models;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp;

public class McpToolSource : IToolSource
{
    private readonly IMcpServerManager _manager;

    private static readonly string[] ManagementToolNames =
    [
        "mcp_list", "mcp_status", "mcp_tools", "mcp_install", "mcp_uninstall",
        "mcp_enable", "mcp_disable", "mcp_restart", "mcp_hot_reload", "mcp_refresh",
        "mcp_oauth_start", "mcp_oauth_complete"
    ];

    public McpToolSource(IMcpServerManager manager)
    {
        _manager = manager;
    }

    public string Name => "mcp";

    public IReadOnlyList<ToolSchema> GetTools()
    {
        var tools = new List<ToolSchema>();

        // 12 management tools
        tools.Add(new ToolSchema("mcp_list", "List all MCP servers and their status.", JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement));
        tools.Add(new ToolSchema("mcp_status", "Get status of an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_tools", "List tools from an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_install", "Install an MCP server. Args: package (string), name (string, optional).", JsonDocument.Parse("""{"type":"object","properties":{"package":{"type":"string"},"name":{"type":"string"}},"required":["package"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_uninstall", "Uninstall an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_enable", "Enable an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_disable", "Disable an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_restart", "Restart an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_hot_reload", "Hot-reload an MCP server config. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_refresh", "Refresh tools from an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_oauth_start", "Start OAuth flow for an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_oauth_complete", "Complete OAuth flow. Args: name (string), code (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"},"code":{"type":"string"}},"required":["name","code"]}""").RootElement));

        // Discovered MCP server tools (namespaced as server__tool)
        foreach (var (serverName, tool) in _manager.GetAllDiscoveredTools())
        {
            tools.Add(new ToolSchema($"{serverName}__{tool.Name}", tool.Description, tool.InputSchema));
        }

        return tools;
    }

    public bool CanHandle(string toolName)
    {
        if (ManagementToolNames.Contains(toolName)) return true;
        // Namespaced MCP tools contain "__"
        if (toolName.Contains("__"))
        {
            var parts = toolName.Split("__", 2);
            return _manager.GetAllDiscoveredTools().Any(t => t.ServerName == parts[0] && t.Tool.Name == parts[1]);
        }
        return false;
    }

    public async Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        // Management tools
        switch (toolName)
        {
            case "mcp_list":
                var servers = await _manager.ListServersAsync(ct);
                return new ToolResult(true, servers.Count == 0
                    ? "No MCP servers configured."
                    : string.Join("\n", servers.Select(s => $"{s.Name} ({s.ServerType}) enabled={s.Enabled}")));

            case "mcp_status":
                var statusName = args.TryGetValue("name", out var sn) ? sn.GetString()! : "";
                var status = await _manager.GetStatusAsync(statusName, ct);
                return new ToolResult(true, $"{status.Name}: {status.Status}, {status.ToolCount} tools");

            case "mcp_tools":
                var toolsName = args.TryGetValue("name", out var tn) ? tn.GetString()! : "";
                var discoveredTools = _manager.GetAllDiscoveredTools()
                    .Where(t => t.ServerName == toolsName).Select(t => t.Tool.Name);
                return new ToolResult(true, string.Join("\n", discoveredTools));

            case "mcp_install":
                var pkg = args.TryGetValue("package", out var pe) ? pe.GetString()! : "";
                var installName = args.TryGetValue("name", out var ine) ? ine.GetString() : null;
                await _manager.InstallAsync(pkg, installName, ct);
                return new ToolResult(true, $"Installed {pkg}");

            case "mcp_uninstall":
                var unName = args.TryGetValue("name", out var une) ? une.GetString()! : "";
                await _manager.UninstallAsync(unName, ct);
                return new ToolResult(true, $"Uninstalled {unName}");

            case "mcp_enable":
                var enName = args.TryGetValue("name", out var ene) ? ene.GetString()! : "";
                await _manager.EnableAsync(enName, ct);
                return new ToolResult(true, $"Enabled {enName}");

            case "mcp_disable":
                var diName = args.TryGetValue("name", out var dne) ? dne.GetString()! : "";
                await _manager.DisableAsync(diName, ct);
                return new ToolResult(true, $"Disabled {diName}");

            case "mcp_restart":
                var reName = args.TryGetValue("name", out var rne) ? rne.GetString()! : "";
                await _manager.RestartAsync(reName, ct);
                return new ToolResult(true, $"Restarted {reName}");

            case "mcp_hot_reload":
                var hrName = args.TryGetValue("name", out var hre) ? hre.GetString()! : "";
                await _manager.HotReloadAsync(hrName, ct);
                return new ToolResult(true, $"Hot-reloaded {hrName}");

            case "mcp_refresh":
                var rfName = args.TryGetValue("name", out var rfe) ? rfe.GetString()! : "";
                await _manager.RefreshToolsAsync(rfName, ct);
                return new ToolResult(true, $"Refreshed tools for {rfName}");

            case "mcp_oauth_start":
                var oaName = args.TryGetValue("name", out var oan) ? oan.GetString()! : "";
                var authUrl = await _manager.OAuthStartAsync(oaName, ct);
                return new ToolResult(true, $"Authorize at: {authUrl}");

            case "mcp_oauth_complete":
                var ocName = args.TryGetValue("name", out var ocn) ? ocn.GetString()! : "";
                var code = args.TryGetValue("code", out var occ) ? occ.GetString()! : "";
                await _manager.OAuthCompleteAsync(ocName, code, ct);
                return new ToolResult(true, $"OAuth completed for {ocName}");
        }

        // Namespaced tool: server__tool
        if (toolName.Contains("__"))
        {
            var parts = toolName.Split("__", 2);
            return await _manager.CallToolAsync(parts[0], parts[1], args, ct);
        }

        return new ToolResult(false, "", $"Unknown MCP tool: {toolName}");
    }
}
