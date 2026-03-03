using Clara.Core.Llm;

namespace Clara.Core.Memory;

/// <summary>
/// Extracts discrete facts from a conversation using an LLM.
/// </summary>
public class MemoryExtractor
{
    private readonly ILlmProvider _llm;
    private readonly string _model;

    private const string ExtractionPrompt = """
        Extract distinct facts from the following conversation. Return each fact on its own line.
        Focus on:
        - Personal preferences and interests
        - Important dates and events
        - Relationships and people mentioned
        - Goals and plans
        - Skills and expertise
        - Location and context information

        Return ONLY the facts, one per line. No numbering, no bullets, no explanations.
        If no facts can be extracted, return an empty response.
        """;

    public MemoryExtractor(ILlmProvider llm, string model = "gpt-4o-mini")
    {
        _llm = llm;
        _model = model;
    }

    public async Task<IReadOnlyList<string>> ExtractFactsAsync(
        IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0)
            return [];

        // Build conversation text
        var conversationText = string.Join("\n", messages.Select(m =>
        {
            var role = m.Role.ToString().ToLowerInvariant();
            var text = string.Join(" ", m.Content.OfType<TextContent>().Select(t => t.Text));
            return $"{role}: {text}";
        }));

        var request = new LlmRequest(
            _model,
            [
                LlmMessage.System(ExtractionPrompt),
                LlmMessage.User(conversationText),
            ],
            MaxTokens: 1024,
            Temperature: 0.0f);

        var response = await _llm.CompleteAsync(request, ct);

        var text = string.Join("", response.Content.OfType<TextContent>().Select(t => t.Text));

        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }
}
