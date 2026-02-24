using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Tools;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

public class ToolRegistryTests
{
    private readonly ToolRegistry _registry = new(NullLogger<ToolRegistry>.Instance);
    private readonly ToolCallContext _ctx = new("user-1", "ch-1", "discord", "req-1");

    private static ToolSchema MakeSchema(string name) =>
        new(name, $"Description for {name}", JsonDocument.Parse("{}").RootElement);

    [Fact]
    public void RegisterTool_And_GetAllTools_ReturnsIt()
    {
        _registry.RegisterTool("test_tool", MakeSchema("test_tool"),
            ctx => Task.FromResult(new ToolResult(true, "ok")));

        var tools = _registry.GetAllTools();
        Assert.Single(tools);
        Assert.Equal("test_tool", tools[0].Name);
    }

    [Fact]
    public void RegisterTool_DuplicateName_Throws()
    {
        _registry.RegisterTool("dupe", MakeSchema("dupe"),
            ctx => Task.FromResult(new ToolResult(true, "ok")));

        Assert.Throws<InvalidOperationException>(() =>
            _registry.RegisterTool("dupe", MakeSchema("dupe"),
                ctx => Task.FromResult(new ToolResult(true, "ok"))));
    }

    [Fact]
    public void UnregisterTool_RemovesIt()
    {
        _registry.RegisterTool("removeme", MakeSchema("removeme"),
            ctx => Task.FromResult(new ToolResult(true, "ok")));

        _registry.UnregisterTool("removeme");

        Assert.Empty(_registry.GetAllTools());
    }

    [Fact]
    public async Task ExecuteAsync_CallsHandler()
    {
        _registry.RegisterTool("echo", MakeSchema("echo"),
            ctx => Task.FromResult(new ToolResult(true, $"hello {ctx.UserId}")));

        var result = await _registry.ExecuteAsync("echo", new(), _ctx);

        Assert.True(result.Success);
        Assert.Equal("hello user-1", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsError()
    {
        var result = await _registry.ExecuteAsync("nonexistent", new(), _ctx);

        Assert.False(result.Success);
        Assert.Contains("nonexistent", result.Error);
    }

    [Fact]
    public void RegisterSource_ToolsAppearInGetAllTools()
    {
        var source = new FakeToolSource("fake", new[] { MakeSchema("fake__alpha"), MakeSchema("fake__beta") });
        _registry.RegisterSource(source);

        var tools = _registry.GetAllTools();
        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public async Task ExecuteAsync_DelegatesToSource()
    {
        var source = new FakeToolSource("fake", new[] { MakeSchema("fake__alpha") });
        _registry.RegisterSource(source);

        var result = await _registry.ExecuteAsync("fake__alpha", new(), _ctx);

        Assert.True(result.Success);
        Assert.Equal("fake handled fake__alpha", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_DirectToolTakesPrecedenceOverSource()
    {
        var source = new FakeToolSource("fake", new[] { MakeSchema("overlap") });
        _registry.RegisterSource(source);

        _registry.RegisterTool("overlap", MakeSchema("overlap"),
            ctx => Task.FromResult(new ToolResult(true, "direct wins")));

        var result = await _registry.ExecuteAsync("overlap", new(), _ctx);

        Assert.True(result.Success);
        Assert.Equal("direct wins", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerException_ReturnsError()
    {
        _registry.RegisterTool("boom", MakeSchema("boom"),
            ctx => { throw new InvalidOperationException("kaboom"); });

        var result = await _registry.ExecuteAsync("boom", new(), _ctx);

        Assert.False(result.Success);
        Assert.Contains("kaboom", result.Error);
    }

    [Fact]
    public void GetAllTools_WithFilter_NotImplementedReturnsAll()
    {
        _registry.RegisterTool("a", MakeSchema("a"),
            ctx => Task.FromResult(new ToolResult(true, "ok")));

        var tools = _registry.GetAllTools(new ToolFilter("discord"));
        Assert.Single(tools);
    }

    private class FakeToolSource : IToolSource
    {
        private readonly ToolSchema[] _tools;
        public string Name { get; }

        public FakeToolSource(string name, ToolSchema[] tools)
        {
            Name = name;
            _tools = tools;
        }

        public IReadOnlyList<ToolSchema> GetTools() => _tools;

        public bool CanHandle(string toolName) =>
            _tools.Any(t => t.Name == toolName);

        public Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
            ToolCallContext context, CancellationToken ct = default) =>
            Task.FromResult(new ToolResult(true, $"fake handled {toolName}"));
    }
}
