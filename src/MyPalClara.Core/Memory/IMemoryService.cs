namespace MyPalClara.Core.Memory;

/// <summary>
/// Interface for the top-level memory orchestrator.
/// Defined in Core so adapters/Gateway can reference it without depending on Memory module.
/// </summary>
public interface IMemoryService
{
    Task<MemoryContext> FetchContextAsync(string query, IReadOnlyList<string> userIds, CancellationToken ct = default);
    List<string> BuildPromptSections(MemoryContext ctx);
    Task AddAsync(string userMessage, string assistantResponse, string userId, CancellationToken ct = default);
    void TrackSentiment(string userId, string channelId, string message);
    Task FinalizeSessionAsync(string userId, string channelId, CancellationToken ct = default);
    Task PromoteUsedMemoriesAsync(IEnumerable<string> memoryIds, IReadOnlyList<string> userIds, CancellationToken ct = default);
    Task<List<MemoryItem>> SearchAsync(string query, IReadOnlyList<string> userIds, int limit = 10, CancellationToken ct = default);
    Task<List<MemoryItem>> GetKeyMemoriesAsync(IReadOnlyList<string> userIds, CancellationToken ct = default);
}
