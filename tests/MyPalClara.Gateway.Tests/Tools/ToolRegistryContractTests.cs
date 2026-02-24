using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

public class ToolRegistryContractTests
{
    [Fact]
    public void ToolCallContext_RecordProperties()
    {
        var ctx = new ToolCallContext("user-1", "ch-1", "discord", "req-1");
        Assert.Equal("user-1", ctx.UserId);
        Assert.Equal("ch-1", ctx.ChannelId);
        Assert.Equal("discord", ctx.Platform);
        Assert.Equal("req-1", ctx.RequestId);
    }

    [Fact]
    public void ToolResult_Success()
    {
        var result = new ToolResult(true, "hello");
        Assert.True(result.Success);
        Assert.Equal("hello", result.Output);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ToolResult_Failure()
    {
        var result = new ToolResult(false, "", "boom");
        Assert.False(result.Success);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void ToolFilter_DefaultsToNull()
    {
        var filter = new ToolFilter();
        Assert.Null(filter.Platform);
        Assert.Null(filter.Capabilities);
    }

    [Fact]
    public void ToolFilter_WithValues()
    {
        var filter = new ToolFilter("discord", new List<string> { "files" });
        Assert.Equal("discord", filter.Platform);
        Assert.Single(filter.Capabilities!);
    }

    [Fact]
    public void IToolRegistry_InterfaceShape()
    {
        var type = typeof(IToolRegistry);
        Assert.NotNull(type.GetMethod("RegisterTool"));
        Assert.NotNull(type.GetMethod("RegisterSource"));
        Assert.NotNull(type.GetMethod("UnregisterTool"));
        Assert.NotNull(type.GetMethod("GetAllTools"));
        Assert.NotNull(type.GetMethod("ExecuteAsync"));
    }

    [Fact]
    public void IToolSource_InterfaceShape()
    {
        var type = typeof(IToolSource);
        Assert.NotNull(type.GetProperty("Name"));
        Assert.NotNull(type.GetMethod("GetTools"));
        Assert.NotNull(type.GetMethod("CanHandle"));
        Assert.NotNull(type.GetMethod("ExecuteAsync"));
    }
}
