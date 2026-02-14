using MyPalClara.Core.Llm;

namespace MyPalClara.Core.Tests;

public class TokenEstimatorTests
{
    [Fact]
    public void Estimate_NullText_ReturnsZero()
    {
        Assert.Equal(0, TokenEstimator.Estimate(null));
    }

    [Fact]
    public void Estimate_EmptyText_ReturnsZero()
    {
        Assert.Equal(0, TokenEstimator.Estimate(""));
    }

    [Theory]
    [InlineData("Hi", 1)]        // 2 chars → ceil(2/4) = 1
    [InlineData("Hello", 2)]     // 5 chars → ceil(5/4) = 2
    [InlineData("test", 1)]      // 4 chars → ceil(4/4) = 1
    [InlineData("Hello, World!", 4)] // 13 chars → ceil(13/4) = 4
    public void Estimate_KnownStrings_ReturnsExpected(string text, int expected)
    {
        Assert.Equal(expected, TokenEstimator.Estimate(text));
    }

    [Fact]
    public void EstimateMessages_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, TokenEstimator.EstimateMessages([]));
    }

    [Fact]
    public void EstimateMessages_IncludesPerMessageOverhead()
    {
        var messages = new List<ChatMessage>
        {
            new UserMessage("test"),
        };

        var result = TokenEstimator.EstimateMessages(messages);

        // "test" = 1 token + 4 per-message overhead = 5
        Assert.Equal(5, result);
    }

    [Fact]
    public void EstimateMessages_MultipleMessages_Sums()
    {
        var messages = new List<ChatMessage>
        {
            new SystemMessage("You are helpful."),   // 16 chars = 4 tokens + 4 = 8
            new UserMessage("test"),                  // 4 chars = 1 token + 4 = 5
        };

        var result = TokenEstimator.EstimateMessages(messages);

        Assert.Equal(13, result);
    }
}
