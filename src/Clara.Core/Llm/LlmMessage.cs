namespace Clara.Core.Llm;

public record LlmMessage(LlmRole Role, IReadOnlyList<LlmContent> Content)
{
    public static LlmMessage User(string text) => new(LlmRole.User, [new TextContent(text)]);
    public static LlmMessage Assistant(string text) => new(LlmRole.Assistant, [new TextContent(text)]);
    public static LlmMessage System(string text) => new(LlmRole.System, [new TextContent(text)]);
}
