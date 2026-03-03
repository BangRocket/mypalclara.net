namespace Clara.Core.Llm;

public record LlmRequest(
    string Model,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools = null,
    float Temperature = 0.7f,
    int? MaxTokens = null);
