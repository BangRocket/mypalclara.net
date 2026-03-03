namespace Clara.Core.Llm.ToolCalling;

public static class ToolCallParser
{
    public static IReadOnlyList<ToolCallContent> ExtractToolCalls(LlmResponse response)
    {
        return response.Content.OfType<ToolCallContent>().ToList();
    }
}
