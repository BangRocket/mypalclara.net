using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Modules.Mcp;
using MyPalClara.Modules.Mcp.Models;

namespace MyPalClara.Modules.Mcp.Tests;

public class McpServerManagerTests
{
    private readonly McpServerManager _manager;

    public McpServerManagerTests()
    {
        _manager = new McpServerManager(NullLogger<McpServerManager>.Instance);
    }

    [Fact]
    public async Task ListServersAsync_InitiallyEmpty()
    {
        var servers = await _manager.ListServersAsync();
        Assert.Empty(servers);
    }

    [Fact]
    public void GetAllDiscoveredTools_InitiallyEmpty()
    {
        var tools = _manager.GetAllDiscoveredTools();
        Assert.Empty(tools);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsStoppedForUnknown()
    {
        var status = await _manager.GetStatusAsync("nonexistent");
        Assert.Equal("nonexistent", status.Name);
        Assert.Equal("stopped", status.Status);
        Assert.Equal(0, status.ToolCount);
    }

    [Fact]
    public async Task EnableAsync_SetsStatus()
    {
        await _manager.EnableAsync("test_server");
        var status = await _manager.GetStatusAsync("test_server");
        Assert.Equal("enabled", status.Status);
    }

    [Fact]
    public async Task DisableAsync_SetsStatus()
    {
        await _manager.EnableAsync("test_server");
        await _manager.DisableAsync("test_server");
        var status = await _manager.GetStatusAsync("test_server");
        Assert.Equal("disabled", status.Status);
    }

    [Fact]
    public async Task CallToolAsync_ReturnsResult()
    {
        var result = await _manager.CallToolAsync("srv", "tool", new Dictionary<string, JsonElement>());
        Assert.True(result.Success);
        Assert.Contains("Called srv/tool", result.Output);
    }

    [Fact]
    public async Task OAuthStartAsync_ReturnsUrl()
    {
        var url = await _manager.OAuthStartAsync("test_server");
        Assert.Contains("test_server", url);
        Assert.StartsWith("https://", url);
    }

    [Fact]
    public async Task OAuthStatusAsync_ReturnsNone()
    {
        var status = await _manager.OAuthStatusAsync("test_server");
        Assert.Equal("none", status);
    }
}
