namespace Clara.Core.Llm;

public record LlmResponse(
    IReadOnlyList<LlmContent> Content,
    string? StopReason,
    LlmUsage Usage);

public record LlmUsage(int InputTokens, int OutputTokens);
