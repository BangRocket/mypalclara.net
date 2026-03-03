using System.Text.Json;
using Clara.Core.Llm;
using Clara.Core.Llm.Providers;
using OpenAI.Chat;

namespace Clara.Core.Tests.Llm;

public class OpenAiCompatProviderTests
{
    [Fact]
    public void ConvertMessages_MapsSystemMessage()
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.System("You are a helpful assistant."),
            LlmMessage.User("Hello"),
        };

        var result = OpenAiCompatProvider.ConvertMessages(messages);

        Assert.Equal(2, result.Count);
        Assert.IsType<SystemChatMessage>(result[0]);
        Assert.IsType<UserChatMessage>(result[1]);
    }

    [Fact]
    public void ConvertMessages_MapsUserAndAssistant()
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.User("Hi"),
            LlmMessage.Assistant("Hello!"),
        };

        var result = OpenAiCompatProvider.ConvertMessages(messages);

        Assert.Equal(2, result.Count);
        Assert.IsType<UserChatMessage>(result[0]);
        Assert.IsType<AssistantChatMessage>(result[1]);
    }

    [Fact]
    public void ConvertMessages_MapsToolResult()
    {
        var toolResult = new LlmMessage(LlmRole.Tool,
        [
            new ToolResultContent("call_123", "result data")
        ]);

        var result = OpenAiCompatProvider.ConvertMessages([toolResult]);

        Assert.Single(result);
        Assert.IsType<ToolChatMessage>(result[0]);
    }

    [Fact]
    public void ConvertMessages_MapsToolCalls()
    {
        var args = JsonDocument.Parse("""{"query": "test"}""").RootElement;
        var assistant = new LlmMessage(LlmRole.Assistant,
        [
            new ToolCallContent("call_123", "search", args)
        ]);

        var result = OpenAiCompatProvider.ConvertMessages([assistant]);

        Assert.Single(result);
        Assert.IsType<AssistantChatMessage>(result[0]);
    }

    [Fact]
    public void ConvertMessages_PreservesMessageOrder()
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.System("system"),
            LlmMessage.User("user1"),
            LlmMessage.Assistant("assistant1"),
            LlmMessage.User("user2"),
        };

        var result = OpenAiCompatProvider.ConvertMessages(messages);

        Assert.Equal(4, result.Count);
        Assert.IsType<SystemChatMessage>(result[0]);
        Assert.IsType<UserChatMessage>(result[1]);
        Assert.IsType<AssistantChatMessage>(result[2]);
        Assert.IsType<UserChatMessage>(result[3]);
    }
}
