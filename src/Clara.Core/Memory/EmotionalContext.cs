namespace Clara.Core.Memory;

public enum Sentiment { Positive, Negative, Neutral }

/// <summary>
/// Simple keyword-based sentiment tracking for a conversation.
/// </summary>
public class EmotionalContext
{
    private static readonly HashSet<string> PositiveWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "happy", "great", "wonderful", "awesome", "excellent", "love", "thanks",
        "thank", "good", "amazing", "fantastic", "perfect", "glad", "excited",
        "pleased", "joy", "beautiful", "brilliant", "helpful", "appreciate"
    };

    private static readonly HashSet<string> NegativeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "sad", "angry", "frustrated", "terrible", "awful", "hate", "bad",
        "horrible", "annoying", "disappointed", "upset", "worried", "anxious",
        "stressed", "confused", "lost", "broken", "wrong", "fail", "error"
    };

    public Sentiment CurrentSentiment { get; private set; } = Sentiment.Neutral;

    public void Update(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var words = message.Split(
            [' ', ',', '.', '!', '?', '\n', '\r', '\t', ';', ':'],
            StringSplitOptions.RemoveEmptyEntries);

        var positiveCount = words.Count(w => PositiveWords.Contains(w));
        var negativeCount = words.Count(w => NegativeWords.Contains(w));

        if (positiveCount > negativeCount)
            CurrentSentiment = Sentiment.Positive;
        else if (negativeCount > positiveCount)
            CurrentSentiment = Sentiment.Negative;
        else
            CurrentSentiment = Sentiment.Neutral;
    }
}
