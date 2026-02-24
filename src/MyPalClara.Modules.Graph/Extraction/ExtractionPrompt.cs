using MyPalClara.Llm;

namespace MyPalClara.Modules.Graph.Extraction;

public static class ExtractionPrompt
{
    public static IReadOnlyList<LlmMessage> Build(string userMessage, string assistantMessage)
    {
        var system = """
            Extract factual relationships as (subject, predicate, object) triples.
            Only extract concrete, verifiable facts. Ignore opinions, questions, and speculation.
            Return JSON array: [{"subject": "...", "predicate": "...", "object": "..."}]
            If no facts found, return [].
            """;

        var user = $"User: {userMessage}\nAssistant: {assistantMessage}";

        return new LlmMessage[] { new SystemMessage(system), new UserMessage(user) };
    }
}
