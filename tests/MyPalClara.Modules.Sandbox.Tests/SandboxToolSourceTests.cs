using System.Text.Json;
using MyPalClara.Modules.Sandbox;
using MyPalClara.Modules.Sandbox.Models;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Sandbox.Tests;

public class SandboxToolSourceTests
{
    [Fact]
    public void GetTools_Returns8Tools()
    {
        var source = new SandboxToolSource(new FakeSandboxManager());
        var tools = source.GetTools();
        Assert.Equal(8, tools.Count);
    }

    [Fact]
    public void CanHandle_RecognizesSandboxTools()
    {
        var source = new SandboxToolSource(new FakeSandboxManager());
        Assert.True(source.CanHandle("execute_python"));
        Assert.True(source.CanHandle("run_shell"));
        Assert.True(source.CanHandle("web_search"));
        Assert.False(source.CanHandle("unknown_tool"));
    }

    [Fact]
    public async Task ExecuteAsync_ExecutePython_ReturnsResult()
    {
        var source = new SandboxToolSource(new FakeSandboxManager());
        var ctx = new ToolCallContext("u1", "c1", "discord", "r1");

        var args = new Dictionary<string, JsonElement>
        {
            ["code"] = JsonDocument.Parse("\"print('hello')\"").RootElement
        };
        var result = await source.ExecuteAsync("execute_python", args, ctx);
        Assert.True(result.Success);
    }

    private class FakeSandboxManager : ISandboxManager
    {
        public Task<ExecutionResult> ExecuteAsync(string userId, string command,
            string? workingDir = null, int? timeoutSeconds = null, CancellationToken ct = default)
            => Task.FromResult(new ExecutionResult(0, "output", "", true));

        public Task<ExecutionResult> ExecutePythonAsync(string userId, string code,
            int? timeoutSeconds = null, CancellationToken ct = default)
            => Task.FromResult(new ExecutionResult(0, "output", "", true));

        public Task<string> WriteFileAsync(string userId, string path, string content, CancellationToken ct = default)
            => Task.FromResult(path);

        public Task<string> ReadFileAsync(string userId, string path, CancellationToken ct = default)
            => Task.FromResult("file content");

        public Task<string[]> ListFilesAsync(string userId, string path, CancellationToken ct = default)
            => Task.FromResult(new[] { "file1.py", "file2.txt" });
    }
}
