using System.Text;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Memory;
using MyPalClara.Memory.Context;
using MyPalClara.Memory.Extraction;
using MyPalClara.Memory.History;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory;

/// <summary>
/// Top-level memory orchestrator. Ties together semantic memory store,
/// mem0-style pipeline, emotional context, and topic recurrence.
/// READ methods accept IReadOnlyList&lt;string&gt; userIds for cross-platform identity.
/// WRITE methods use single userId (platform-specific).
/// </summary>
public sealed class MemoryService : IMemoryService
{
    private readonly ISemanticMemoryStore _store;
    private readonly EmbeddingClient _embeddingClient;
    private readonly FactExtractor _factExtractor;
    private readonly MemoryManager _memoryManager;
    private readonly MemoryHistoryStore _historyStore;
    private readonly ClaraConfig _config;
    private readonly ILogger<MemoryService> _logger;

    // Optional subsystems — null when not configured
    private readonly EmotionalContext? _emotionalContext;
    private readonly TopicRecurrence? _topicRecurrence;

    public MemoryService(
        ISemanticMemoryStore store,
        EmbeddingClient embeddingClient,
        FactExtractor factExtractor,
        MemoryManager memoryManager,
        MemoryHistoryStore historyStore,
        ClaraConfig config,
        ILogger<MemoryService> logger,
        EmotionalContext? emotionalContext = null,
        TopicRecurrence? topicRecurrence = null)
    {
        _store = store;
        _embeddingClient = embeddingClient;
        _factExtractor = factExtractor;
        _memoryManager = memoryManager;
        _historyStore = historyStore;
        _config = config;
        _logger = logger;
        _emotionalContext = emotionalContext;
        _topicRecurrence = topicRecurrence;
    }

    /// <summary>
    /// Fetch all memory context for a query across linked user IDs.
    /// Runs parallel: key memories, vector search, graph, emotional, topics.
    /// </summary>
    public async Task<MemoryContext> FetchContextAsync(string query, IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        var embedding = await _embeddingClient.EmbedAsync(query, ct);

        // Parallel retrieval — core
        var keyMemoriesTask = _store.GetAllMemoriesAsync(
            userIds,
            new Dictionary<string, object?> { ["is_key"] = "true" },
            limit: 15, ct: ct);

        var searchTask = _store.SearchAsync(embedding, userIds, limit: 35, ct: ct);

        // Parallel retrieval — optional subsystems
        var graphTask = _store.SearchEntitiesAsync(query, userIds, embedding, ct: ct);
        var emotionalTask = _emotionalContext?.RetrieveAsync(userIds, ct: ct);
        var topicsTask = _topicRecurrence?.GetRecurringTopicsAsync(userIds, ct: ct);

        await Task.WhenAll(
            keyMemoriesTask,
            searchTask,
            graphTask,
            emotionalTask ?? Task.CompletedTask,
            topicsTask ?? Task.CompletedTask);

        var keyMemories = await keyMemoriesTask;
        var searchResults = await searchTask;

        // Sort by vector score (no FSRS rerank)
        searchResults = searchResults.OrderByDescending(m => m.Score).ToList();

        var graphRelations = await graphTask;
        var emotionalCtx = emotionalTask is not null ? await emotionalTask : [];
        var recurringTopics = topicsTask is not null ? await topicsTask : [];

        _logger.LogDebug(
            "Memory context: {KeyCount} key, {SearchCount} search, {GraphCount} graph, {EmotionalCount} emotional, {TopicCount} topics",
            keyMemories.Count, searchResults.Count, graphRelations.Count, emotionalCtx.Count, recurringTopics.Count);

        return new MemoryContext
        {
            KeyMemories = keyMemories,
            RelevantMemories = searchResults,
            GraphRelations = graphRelations,
            EmotionalContext = emotionalCtx,
            RecurringTopics = recurringTopics,
        };
    }

    /// <summary>Build system message sections from memory context.</summary>
    public static List<string> BuildPromptSections(MemoryContext ctx)
    {
        var sections = new List<string>();

        if (ctx.KeyMemories.Count > 0)
        {
            var sb = new StringBuilder("KEY MEMORIES (always relevant):\n");
            foreach (var m in ctx.KeyMemories)
                sb.AppendLine($"- [KEY] {m.Memory}");
            sections.Add(sb.ToString());
        }

        if (ctx.RelevantMemories.Count > 0)
        {
            var sb = new StringBuilder("RELEVANT MEMORIES:\n");
            foreach (var m in ctx.RelevantMemories.Take(20))
                sb.AppendLine($"- {m.Memory}");
            sections.Add(sb.ToString());
        }

        if (ctx.GraphRelations.Count > 0)
        {
            var sb = new StringBuilder("KNOWN RELATIONSHIPS:\n");
            foreach (var r in ctx.GraphRelations.Take(20))
                sb.AppendLine($"- {r}");
            sections.Add(sb.ToString());
        }

        if (ctx.EmotionalContext.Count > 0)
        {
            var sb = new StringBuilder("RECENT EMOTIONAL CONTEXT:\n");
            foreach (var e in ctx.EmotionalContext.Take(3))
                sb.AppendLine($"- {e}");
            sections.Add(sb.ToString());
        }

        if (ctx.RecurringTopics.Count > 0)
        {
            var sb = new StringBuilder("RECURRING TOPICS:\n");
            foreach (var t in ctx.RecurringTopics.Take(3))
                sb.AppendLine($"- {t}");
            sections.Add(sb.ToString());
        }

        return sections;
    }

    // IMemoryService.BuildPromptSections is not static, so implement both
    List<string> IMemoryService.BuildPromptSections(MemoryContext ctx) => BuildPromptSections(ctx);

    /// <summary>Store new memories from a conversation (background, post-response). WRITE — single userId.</summary>
    public async Task AddAsync(string userMessage, string assistantResponse, string userId, CancellationToken ct = default)
    {
        List<string> facts = [];

        // 1. Extract facts via LLM
        try
        {
            facts = await _factExtractor.ExtractFactsAsync(userMessage, assistantResponse, ct);
            _logger.LogDebug("Extracted {Count} facts for ingestion", facts.Count);

            // 2. Process facts through mem0-style pipeline
            if (facts.Count > 0)
            {
                var actions = await _memoryManager.ProcessFactsAsync(facts, userId, ct: ct);
                _logger.LogDebug("Memory pipeline produced {Count} actions", actions.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fact extraction/ingestion failed for user {UserId}", userId);
        }

        // 3. Graph extraction — feed structured facts instead of raw conversation text
        var graphTask = Task.CompletedTask;
        var topicTask = Task.CompletedTask;

        if (facts.Count > 0)
        {
            try
            {
                var factText = string.Join("\n", facts);
                graphTask = _store.AddEntityDataAsync(factText, userId, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Graph extraction failed");
            }
        }

        // 4. Topic extraction + storage
        if (_topicRecurrence is not null)
        {
            topicTask = ExtractAndStoreTopicsAsync(userMessage, assistantResponse, userId, ct);
        }

        await Task.WhenAll(graphTask, topicTask);
    }

    private async Task ExtractAndStoreTopicsAsync(
        string userMessage, string assistantResponse, string userId, CancellationToken ct)
    {
        try
        {
            var conversationText = $"User: {userMessage}\nAssistant: {assistantResponse}";
            var topics = await _topicRecurrence!.ExtractTopicsAsync(conversationText, ct);
            foreach (var topic in topics)
                await _topicRecurrence.StoreMentionAsync(topic, userId, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Topic extraction failed for user {UserId}", userId);
        }
    }

    /// <summary>Track emotional sentiment for a message. WRITE — single userId.</summary>
    public void TrackSentiment(string userId, string channelId, string message)
    {
        _emotionalContext?.TrackMessage(userId, channelId, message);
    }

    /// <summary>Finalize and persist emotional context for a session. WRITE — single userId.</summary>
    public async Task FinalizeSessionAsync(string userId, string channelId, CancellationToken ct = default)
    {
        if (_emotionalContext is null) return;

        try
        {
            await _emotionalContext.FinalizeSessionAsync(userId, channelId, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to finalize emotional session");
        }
    }

    /// <summary>Search memories across linked user IDs (for !memory search command).</summary>
    public async Task<List<MemoryItem>> SearchAsync(string query, IReadOnlyList<string> userIds, int limit = 10, CancellationToken ct = default)
    {
        var embedding = await _embeddingClient.EmbedAsync(query, ct);
        var results = await _store.SearchAsync(embedding, userIds, limit: limit, ct: ct);
        return results.OrderByDescending(m => m.Score).ToList();
    }

    /// <summary>Get key memories across linked user IDs (for !memory key command).</summary>
    public Task<List<MemoryItem>> GetKeyMemoriesAsync(IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        return _store.GetAllMemoriesAsync(
            userIds,
            new Dictionary<string, object?> { ["is_key"] = "true" },
            limit: 50, ct: ct);
    }

    /// <summary>Get change history for a memory.</summary>
    public Task<List<MemoryHistoryEntry>> GetHistoryAsync(string memoryId, CancellationToken ct = default)
    {
        return _historyStore.GetHistoryAsync(memoryId, ct);
    }

    /// <summary>Get a single memory by ID.</summary>
    public Task<MemoryItem?> GetAsync(string memoryId, CancellationToken ct = default)
        => _store.GetMemoryAsync(memoryId, ct);

    /// <summary>Get all memories across linked user IDs.</summary>
    public Task<List<MemoryItem>> GetAllAsync(IReadOnlyList<string> userIds, int limit = 100, CancellationToken ct = default)
        => _store.GetAllMemoriesAsync(userIds, limit: limit, ct: ct);

    /// <summary>Delete a memory by ID with history tracking.</summary>
    public async Task DeleteAsync(string memoryId, CancellationToken ct = default)
    {
        var existing = await _store.GetMemoryAsync(memoryId, ct);
        await _store.DeleteMemoryAsync(memoryId, ct);
        await _historyStore.AddEntryAsync(memoryId, existing?.Memory, null, "DELETE", ct: ct);
    }

    /// <summary>Update a memory's text by ID with history tracking.</summary>
    public async Task UpdateAsync(string memoryId, string newText, CancellationToken ct = default)
    {
        var existing = await _store.GetMemoryAsync(memoryId, ct);
        var embedding = await _embeddingClient.EmbedAsync(newText, ct);
        await _store.UpdateMemoryAsync(memoryId, embedding, newText, ct: ct);
        await _historyStore.AddEntryAsync(memoryId, existing?.Memory, newText, "UPDATE", ct: ct);
    }
}
