using System.Text.Json;
using Clara.Core.Tools;
using Clara.Core.Tools.BuiltIn;

namespace Clara.Core.Tests.Tools.BuiltIn;

public class ShellToolTests
{
    private readonly ShellTool _tool = new();
    private static readonly ToolExecutionContext Context = new("user1", "session1", "test", false, null);

    [Fact]
    public async Task Execute_echo_returns_output()
    {
        var args = JsonDocument.Parse("""{"command":"echo hello"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, Context);

        Assert.True(result.Success);
        Assert.Contains("hello", result.Content);
    }

    [Fact]
    public async Task Execute_nonexistent_command_returns_failure()
    {
        var args = JsonDocument.Parse("""{"command":"this_command_does_not_exist_xyz 2>/dev/null"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, Context);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Missing_command_returns_failure()
    {
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await _tool.ExecuteAsync(args, Context);

        Assert.False(result.Success);
        Assert.Contains("command", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_command_returns_failure()
    {
        var args = JsonDocument.Parse("""{"command":""}""").RootElement;

        var result = await _tool.ExecuteAsync(args, Context);

        Assert.False(result.Success);
    }

    [Fact]
    public void Tool_metadata_is_correct()
    {
        Assert.Equal("shell_execute", _tool.Name);
        Assert.Equal(ToolCategory.Shell, _tool.Category);
    }
}
