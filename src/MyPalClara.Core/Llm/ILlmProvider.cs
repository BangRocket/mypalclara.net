namespace MyPalClara.Core.Llm;

/// <summary>Interface for LLM providers.</summary>
public interface ILlmProvider
{
    /// <summary>Non-streaming completion.</summary>
    Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string? model = null,
        int maxTokens = 4096,
        float temperature = 0f,
        CancellationToken ct = default);

    /// <summary>Completion with tool calling support.</summary>
    Task<ToolResponse> CompleteWithToolsAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolSchema> tools,
        string? model = null,
        int maxTokens = 4096,
        float temperature = 0f,
        CancellationToken ct = default);

    /// <summary>Streaming completion — yields text chunks as they arrive.</summary>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string? model = null,
        int maxTokens = 4096,
        float temperature = 0f,
        CancellationToken ct = default);
}
