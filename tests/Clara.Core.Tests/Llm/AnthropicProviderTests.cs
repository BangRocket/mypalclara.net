using Clara.Core.Llm;
using Clara.Core.Llm.Providers;

namespace Clara.Core.Tests.Llm;

public class AnthropicProviderTests
{
    [Fact]
    public void ConvertMessages_ExtractsSystemMessage()
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.System("You are a helpful assistant."),
            LlmMessage.User("Hello"),
            LlmMessage.Assistant("Hi there!"),
        };

        var (system, sdkMessages) = AnthropicProvider.ConvertMessages(messages);

        Assert.Equal("You are a helpful assistant.", system);
        Assert.Equal(2, sdkMessages.Count);
    }

    [Fact]
    public void ConvertMessages_ConcatenatesMultipleSystemMessages()
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.System("Part one."),
            LlmMessage.User("Hello"),
            LlmMessage.System("Part two."),
        };

        var (system, sdkMessages) = AnthropicProvider.ConvertMessages(messages);

        Assert.Equal("Part one.\nPart two.", system);
        Assert.Single(sdkMessages);
    }

    [Fact]
    public void ConvertMessages_NoSystemMessage_ReturnsNull()
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.User("Hello"),
        };

        var (system, sdkMessages) = AnthropicProvider.ConvertMessages(messages);

        Assert.Null(system);
        Assert.Single(sdkMessages);
    }

    [Fact]
    public void ConvertMessages_MapsRolesCorrectly()
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.User("Hi"),
            LlmMessage.Assistant("Hello"),
        };

        var (_, sdkMessages) = AnthropicProvider.ConvertMessages(messages);

        Assert.Equal(Anthropic.MessageRole.User, sdkMessages[0].Role);
        Assert.Equal(Anthropic.MessageRole.Assistant, sdkMessages[1].Role);
    }

    [Fact]
    public void ConvertMessages_ToolResultBecomesUserRole()
    {
        var toolResult = new LlmMessage(LlmRole.Tool,
        [
            new ToolResultContent("tool_123", "result data")
        ]);

        var (_, sdkMessages) = AnthropicProvider.ConvertMessages([toolResult]);

        Assert.Single(sdkMessages);
        Assert.Equal(Anthropic.MessageRole.User, sdkMessages[0].Role);
    }

    [Fact]
    public void ConvertMessages_PreservesTextContent()
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.User("Hello world"),
        };

        var (_, sdkMessages) = AnthropicProvider.ConvertMessages(messages);

        Assert.Single(sdkMessages);
        // Content is OneOf<string, IList<Block>>; the block list should have a TextBlock
        var content = sdkMessages[0].Content;
        Assert.True(content.IsValue2);
        var blocks = content.Value2!;
        Assert.Single(blocks);
        Assert.True(blocks[0].IsText);
        Assert.Equal("Hello world", blocks[0].Text!.Text);
    }
}
