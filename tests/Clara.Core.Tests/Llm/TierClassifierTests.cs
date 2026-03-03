using Clara.Core.Llm;

namespace Clara.Core.Tests.Llm;

public class TierClassifierTests
{
    [Theory]
    [InlineData("Hi", ModelTier.Low)]
    [InlineData("Hello there!", ModelTier.Low)]
    [InlineData("What time is it?", ModelTier.Low)]
    [InlineData("", ModelTier.Low)]
    public void ShortMessages_ReturnLow(string message, ModelTier expected)
    {
        Assert.Equal(expected, TierClassifier.Classify(message));
    }

    [Theory]
    [InlineData("Can you help me write a function to sort an array? Here is my code:\n```python\ndef sort(arr):\n    pass\n```")]
    [InlineData("I have this class definition: public class Foo { }")]
    [InlineData("Please review this code and tell me what you think: import numpy as np")]
    public void CodeContent_ReturnHigh(string message)
    {
        Assert.Equal(ModelTier.High, TierClassifier.Classify(message));
    }

    [Fact]
    public void MediumLengthNonCode_ReturnMid()
    {
        var message = "Can you explain to me how photosynthesis works in plants?";
        Assert.Equal(ModelTier.Mid, TierClassifier.Classify(message));
    }

    [Fact]
    public void NullOrWhitespace_ReturnLow()
    {
        Assert.Equal(ModelTier.Low, TierClassifier.Classify("   "));
    }
}
