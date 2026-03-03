using Clara.Core.Llm;

namespace Clara.Core.Memory;

/// <summary>
/// Generates a concise summary of a conversation session using an LLM.
/// </summary>
public class SessionSummarizer
{
    private readonly ILlmProvider _llm;
    private readonly string _model;

    private const string SummarizePrompt = """
        Summarize the following conversation concisely. Include:
        - Key topics discussed
        - Important decisions or conclusions
        - Action items or follow-ups
        - Emotional tone of the conversation

        Keep the summary to 2-4 sentences.
        """;

    public SessionSummarizer(ILlmProvider llm, string model = "gpt-4o-mini")
    {
        _llm = llm;
        _model = model;
    }

    public async Task<string> SummarizeAsync(
        IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0)
            return "Empty session.";

        var conversationText = string.Join("\n", messages.Select(m =>
        {
            var role = m.Role.ToString().ToLowerInvariant();
            var text = string.Join(" ", m.Content.OfType<TextContent>().Select(t => t.Text));
            return $"{role}: {text}";
        }));

        var request = new LlmRequest(
            _model,
            [
                LlmMessage.System(SummarizePrompt),
                LlmMessage.User(conversationText),
            ],
            MaxTokens: 512,
            Temperature: 0.3f);

        var response = await _llm.CompleteAsync(request, ct);

        return string.Join("", response.Content.OfType<TextContent>().Select(t => t.Text)).Trim();
    }
}
