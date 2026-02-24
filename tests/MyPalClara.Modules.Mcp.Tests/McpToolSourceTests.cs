using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Mcp;
using MyPalClara.Modules.Mcp.Models;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp.Tests;

public class McpToolSourceTests
{
    [Fact]
    public void Name_ReturnsMcp()
    {
        var source = new McpToolSource(new FakeServerManager());
        Assert.Equal("mcp", source.Name);
    }

    [Fact]
    public void GetTools_ReturnsManagementToolsPlusDiscoveredTools()
    {
        var mgr = new FakeServerManager();
        mgr.AddDiscoveredTool("testserver", new McpTool("testtool", "A test tool",
            JsonDocument.Parse("{}").RootElement));

        var source = new McpToolSource(mgr);
        var tools = source.GetTools();

        // 12 management tools + 1 discovered tool
        Assert.True(tools.Count >= 13, $"Expected >= 13 tools, got {tools.Count}");
        Assert.Contains(tools, t => t.Name == "testserver__testtool");
        Assert.Contains(tools, t => t.Name == "mcp_list");
    }

    [Fact]
    public void CanHandle_MatchesNamespacedTools()
    {
        var mgr = new FakeServerManager();
        mgr.AddDiscoveredTool("srv", new McpTool("do_thing", "desc",
            JsonDocument.Parse("{}").RootElement));

        var source = new McpToolSource(mgr);

        Assert.True(source.CanHandle("srv__do_thing"));
        Assert.True(source.CanHandle("mcp_list"));
        Assert.False(source.CanHandle("unknown_tool"));
    }

    [Fact]
    public async Task ExecuteAsync_ManagementTool_Works()
    {
        var mgr = new FakeServerManager();
        var source = new McpToolSource(mgr);
        var ctx = new ToolCallContext("u1", "c1", "discord", "r1");

        var result = await source.ExecuteAsync("mcp_list", new(), ctx);
        Assert.True(result.Success);
    }

    private class FakeServerManager : IMcpServerManager
    {
        private readonly Dictionary<string, List<McpTool>> _discoveredTools = new();

        public void AddDiscoveredTool(string serverName, McpTool tool)
        {
            if (!_discoveredTools.ContainsKey(serverName))
                _discoveredTools[serverName] = [];
            _discoveredTools[serverName].Add(tool);
        }

        public IReadOnlyList<(string ServerName, McpTool Tool)> GetAllDiscoveredTools()
        {
            var result = new List<(string, McpTool)>();
            foreach (var (server, tools) in _discoveredTools)
                foreach (var tool in tools)
                    result.Add((server, tool));
            return result;
        }

        public Task<IReadOnlyList<McpServerConfig>> ListServersAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpServerConfig>>([]);

        public Task<ServerStatus> GetStatusAsync(string serverName, CancellationToken ct = default)
            => Task.FromResult(new ServerStatus(serverName, "stopped", 0, null));

        public Task<ToolResult> CallToolAsync(string serverName, string toolName,
            Dictionary<string, JsonElement> args, CancellationToken ct = default)
            => Task.FromResult(new ToolResult(true, "mock result"));

        public Task InstallAsync(string packageOrUrl, string? name = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task UninstallAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnableAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisableAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task RestartAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task HotReloadAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task RefreshToolsAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> OAuthStartAsync(string name, CancellationToken ct = default)
            => Task.FromResult("https://auth.example.com");
        public Task OAuthCompleteAsync(string name, string code, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> OAuthStatusAsync(string name, CancellationToken ct = default)
            => Task.FromResult("none");
    }
}
