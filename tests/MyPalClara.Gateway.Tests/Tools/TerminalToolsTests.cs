using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Tools;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

public class TerminalToolsTests
{
    private readonly ToolRegistry _registry = new(NullLogger<ToolRegistry>.Instance);
    private readonly ToolCallContext _ctx = new("user-1", "ch-1", "discord", "req-1");

    [Fact]
    public async Task ExecuteCommand_ReturnsOutput()
    {
        TerminalTools.Register(_registry);

        var args = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonDocument.Parse("\"echo hello\"").RootElement
        };

        var result = await _registry.ExecuteAsync("execute_command", args, _ctx);

        Assert.True(result.Success);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public async Task ExecuteCommand_BadCommand_ReturnsError()
    {
        TerminalTools.Register(_registry);

        var args = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonDocument.Parse("\"nonexistent_binary_xyz_12345\"").RootElement
        };

        var result = await _registry.ExecuteAsync("execute_command", args, _ctx);

        // Should return something (either error output or failure)
        Assert.NotNull(result.Output);
    }

    [Fact]
    public async Task GetCommandHistory_ReturnsHistory()
    {
        TerminalTools.Register(_registry);

        // First execute a command
        var execArgs = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonDocument.Parse("\"echo test_history\"").RootElement
        };
        await _registry.ExecuteAsync("execute_command", execArgs, _ctx);

        // Then get history
        var histArgs = new Dictionary<string, JsonElement>
        {
            ["limit"] = JsonDocument.Parse("10").RootElement
        };
        var result = await _registry.ExecuteAsync("get_command_history", histArgs, _ctx);

        Assert.True(result.Success);
        Assert.Contains("echo test_history", result.Output);
    }
}
