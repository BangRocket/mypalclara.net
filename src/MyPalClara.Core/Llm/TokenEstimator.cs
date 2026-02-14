namespace MyPalClara.Core.Llm;

/// <summary>
/// Heuristic token estimator. ~4 chars per token is a reasonable approximation
/// across most LLM tokenizers (GPT, Claude, etc.).
/// </summary>
public static class TokenEstimator
{
    private const int CharsPerToken = 4;
    private const int PerMessageOverhead = 4; // role, formatting tokens

    public static int Estimate(string? text)
        => text is null ? 0 : (text.Length + CharsPerToken - 1) / CharsPerToken;

    public static int EstimateMessages(IReadOnlyList<ChatMessage> messages)
    {
        var total = 0;
        foreach (var msg in messages)
            total += Estimate(msg.Content) + PerMessageOverhead;
        return total;
    }
}
