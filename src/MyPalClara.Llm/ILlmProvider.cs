namespace MyPalClara.Llm;

public interface ILlmProvider
{
    Task<LlmResponse> InvokeAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolSchema>? tools = null,
        CancellationToken ct = default);

    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken ct = default);
}
