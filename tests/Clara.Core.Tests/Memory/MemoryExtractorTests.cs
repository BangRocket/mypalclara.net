using Clara.Core.Llm;
using Clara.Core.Memory;
using Clara.Core.Tests.Llm;

namespace Clara.Core.Tests.Memory;

public class MemoryExtractorTests
{
    [Fact]
    public async Task ExtractFacts_ReturnsFactsFromLlmResponse()
    {
        var llm = new MockLlmProvider("Joshua likes dogs\nJoshua lives in Portland\nJoshua works as a developer");
        var extractor = new MemoryExtractor(llm);

        var messages = new List<LlmMessage>
        {
            LlmMessage.User("Hi, I'm Joshua. I live in Portland and I love dogs."),
            LlmMessage.Assistant("Nice to meet you, Joshua! Portland is a great city for dog lovers.")
        };

        var facts = await extractor.ExtractFactsAsync(messages);

        Assert.Equal(3, facts.Count);
        Assert.Contains("Joshua likes dogs", facts);
        Assert.Contains("Joshua lives in Portland", facts);
    }

    [Fact]
    public async Task ExtractFacts_EmptyMessages_ReturnsEmpty()
    {
        var llm = new MockLlmProvider("");
        var extractor = new MemoryExtractor(llm);

        var facts = await extractor.ExtractFactsAsync([]);

        Assert.Empty(facts);
    }

    [Fact]
    public async Task ExtractFacts_EmptyResponse_ReturnsEmpty()
    {
        var llm = new MockLlmProvider("");
        var extractor = new MemoryExtractor(llm);

        var messages = new List<LlmMessage>
        {
            LlmMessage.User("Hello")
        };

        var facts = await extractor.ExtractFactsAsync(messages);

        Assert.Empty(facts);
    }

    [Fact]
    public async Task ExtractFacts_SkipsBlankLines()
    {
        var llm = new MockLlmProvider("Fact one\n\n\nFact two\n\n");
        var extractor = new MemoryExtractor(llm);

        var messages = new List<LlmMessage>
        {
            LlmMessage.User("Tell me about yourself")
        };

        var facts = await extractor.ExtractFactsAsync(messages);

        Assert.Equal(2, facts.Count);
    }
}

public class SessionSummarizerTests
{
    [Fact]
    public async Task Summarize_ReturnsSummaryText()
    {
        var llm = new MockLlmProvider("The user discussed their preference for dogs and living in Portland.");
        var summarizer = new SessionSummarizer(llm);

        var messages = new List<LlmMessage>
        {
            LlmMessage.User("I like dogs and I live in Portland."),
            LlmMessage.Assistant("That sounds great!")
        };

        var summary = await summarizer.SummarizeAsync(messages);

        Assert.Contains("dogs", summary);
        Assert.Contains("Portland", summary);
    }

    [Fact]
    public async Task Summarize_EmptyMessages_ReturnsDefault()
    {
        var llm = new MockLlmProvider("should not be called");
        var summarizer = new SessionSummarizer(llm);

        var summary = await summarizer.SummarizeAsync([]);

        Assert.Equal("Empty session.", summary);
    }
}

public class EmotionalContextTests
{
    [Fact]
    public void Update_PositiveMessage_SetsPositive()
    {
        var ctx = new EmotionalContext();
        ctx.Update("I'm so happy and excited about this! It's wonderful!");

        Assert.Equal(Sentiment.Positive, ctx.CurrentSentiment);
    }

    [Fact]
    public void Update_NegativeMessage_SetsNegative()
    {
        var ctx = new EmotionalContext();
        ctx.Update("This is terrible and frustrating. I hate it.");

        Assert.Equal(Sentiment.Negative, ctx.CurrentSentiment);
    }

    [Fact]
    public void Update_NeutralMessage_SetsNeutral()
    {
        var ctx = new EmotionalContext();
        ctx.Update("The weather is cloudy today.");

        Assert.Equal(Sentiment.Neutral, ctx.CurrentSentiment);
    }

    [Fact]
    public void Update_EmptyMessage_StaysNeutral()
    {
        var ctx = new EmotionalContext();
        ctx.Update("");

        Assert.Equal(Sentiment.Neutral, ctx.CurrentSentiment);
    }
}
