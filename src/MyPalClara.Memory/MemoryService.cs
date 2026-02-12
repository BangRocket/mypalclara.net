using System.Text;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Memory;
using MyPalClara.Memory.Context;
using MyPalClara.Memory.Dynamics;
using MyPalClara.Memory.Extraction;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory;

/// <summary>
/// Top-level memory orchestrator. Ties together semantic memory store, FSRS,
/// emotional context, and topic recurrence.
/// READ methods accept IReadOnlyList&lt;string&gt; userIds for cross-platform identity.
/// WRITE methods use single userId (platform-specific).
/// </summary>
public sealed class MemoryService : IMemoryService
{
    private readonly ISemanticMemoryStore _store;
    private readonly EmbeddingClient _embeddingClient;
    private readonly MemoryDynamicsService _dynamics;
    private readonly CompositeScorer _scorer;
    private readonly ClaraConfig _config;
    private readonly ILogger<MemoryService> _logger;

    // Optional subsystems — null when not configured
    private readonly EmotionalContext? _emotionalContext;
    private readonly TopicRecurrence? _topicRecurrence;
    private readonly FactExtractor? _factExtractor;
    private readonly SmartIngest? _smartIngest;

    public MemoryService(
        ISemanticMemoryStore store,
        EmbeddingClient embeddingClient,
        MemoryDynamicsService dynamics,
        CompositeScorer scorer,
        ClaraConfig config,
        ILogger<MemoryService> logger,
        EmotionalContext? emotionalContext = null,
        TopicRecurrence? topicRecurrence = null,
        FactExtractor? factExtractor = null,
        SmartIngest? smartIngest = null)
    {
        _store = store;
        _embeddingClient = embeddingClient;
        _dynamics = dynamics;
        _scorer = scorer;
        _config = config;
        _logger = logger;
        _emotionalContext = emotionalContext;
        _topicRecurrence = topicRecurrence;
        _factExtractor = factExtractor;
        _smartIngest = smartIngest;
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

        // FSRS re-rank search results
        searchResults = await _scorer.RankAsync(searchResults, userIds);

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
        // Fact extraction + smart ingest
        if (_factExtractor is not null && _smartIngest is not null)
        {
            try
            {
                var facts = await _factExtractor.ExtractFactsAsync(userMessage, assistantResponse, ct);
                _logger.LogDebug("Extracted {Count} facts for ingestion", facts.Count);

                foreach (var fact in facts)
                {
                    var result = await _smartIngest.IngestAsync(fact, userId, ct);
                    _logger.LogDebug("Ingested fact: {Action} — {Reason}", result.Action, result.Reason);
                }

                // Add facts to graph
                foreach (var fact in facts)
                {
                    try { await _store.AddEntityDataAsync(fact, userId, ct: ct); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Graph add failed for fact"); }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fact extraction/ingestion failed for user {UserId}", userId);
            }
        }

        // Topic extraction + storage
        if (_topicRecurrence is not null)
        {
            try
            {
                var conversationText = $"User: {userMessage}\nAssistant: {assistantResponse}";
                var topics = await _topicRecurrence.ExtractTopicsAsync(conversationText, ct);
                foreach (var topic in topics)
                    await _topicRecurrence.StoreMentionAsync(topic, userId, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Topic extraction failed for user {UserId}", userId);
            }
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

    /// <summary>Promote memories that were used in a response (across linked user IDs).</summary>
    public async Task PromoteUsedMemoriesAsync(IEnumerable<string> memoryIds, IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        foreach (var id in memoryIds)
        {
            try
            {
                await _dynamics.PromoteAsync(id, userIds, Grade.Good, "used_in_response");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to promote memory {MemoryId}", id);
            }
        }
    }

    /// <summary>Search memories across linked user IDs (for !memory search command).</summary>
    public async Task<List<MemoryItem>> SearchAsync(string query, IReadOnlyList<string> userIds, int limit = 10, CancellationToken ct = default)
    {
        var embedding = await _embeddingClient.EmbedAsync(query, ct);
        var results = await _store.SearchAsync(embedding, userIds, limit: limit, ct: ct);
        return await _scorer.RankAsync(results, userIds);
    }

    /// <summary>Get key memories across linked user IDs (for !memory key command).</summary>
    public Task<List<MemoryItem>> GetKeyMemoriesAsync(IReadOnlyList<string> userIds, CancellationToken ct = default)
    {
        return _store.GetAllMemoriesAsync(
            userIds,
            new Dictionary<string, object?> { ["is_key"] = "true" },
            limit: 50, ct: ct);
    }
}
