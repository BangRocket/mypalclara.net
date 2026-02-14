using MyPalClara.Core.Configuration;
using MyPalClara.Core.Llm;

namespace MyPalClara.Agent.Tests;

public class OrchestratorConfigTests
{
    [Fact]
    public void ClaraConfig_MaxToolIterations_HasDefault()
    {
        var config = new ClaraConfig();

        Assert.True(config.Gateway.MaxToolIterations > 0);
    }

    [Fact]
    public void ToolSchema_ToAnthropicFormat_IncludesAllFields()
    {
        var schema = new ToolSchema(
            "test__tool",
            "A test tool",
            System.Text.Json.JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement);

        var format = schema.ToAnthropicFormat();

        Assert.Equal("test__tool", format["name"]);
        Assert.Equal("A test tool", format["description"]);
        Assert.NotNull(format["input_schema"]);
    }

    [Fact]
    public void ToolSchema_ToOpenAiFormat_HasFunctionWrapper()
    {
        var schema = new ToolSchema(
            "test__tool",
            "A test tool",
            System.Text.Json.JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement);

        var format = schema.ToOpenAiFormat();

        Assert.Equal("function", format["type"]);
        Assert.NotNull(format["function"]);
    }
}
