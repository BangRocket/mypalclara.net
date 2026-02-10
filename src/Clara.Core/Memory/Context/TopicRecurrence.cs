using System.Text.Json;
using Clara.Core.Llm;
using Clara.Core.Memory.Vector;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Memory.Context;

/// <summary>
/// Topic extraction and recurrence pattern detection.
/// Port of clara_core/topic_recurrence.py.
/// Uses LLM-based extraction + vector store for persistence.
/// </summary>
public sealed class TopicRecurrence
{
    private readonly RookProvider _rook;
    private readonly IVectorStore _vectorStore;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<TopicRecurrence> _logger;

    public TopicRecurrence(
        RookProvider rook,
        IVectorStore vectorStore,
        EmbeddingClient embeddingClient,
        ILogger<TopicRecurrence> logger)
    {
        _rook = rook;
        _vectorStore = vectorStore;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    /// <summary>Extract topics from a conversation using LLM.</summary>
    public async Task<List<TopicMention>> ExtractTopicsAsync(
        string conversationText, CancellationToken ct = default)
    {
        var prompt = $$"""
            Extract the main topics from this conversation. Return a JSON array (max 3 topics) where each item has:
            - "topic": short topic name
            - "topic_type": "entity" or "theme"
            - "context_snippet": brief relevant quote
            - "emotional_weight": "light", "moderate", or "heavy"

            Conversation:
            {{conversationText}}

            Return ONLY a JSON array, no other text.
            """;

        try
        {
            var response = await _rook.CompleteAsync(
                [new SystemMessage("You extract topics from conversations. Return only JSON arrays."),
                 new UserMessage(prompt)],
                ct: ct);

            var trimmed = response.Trim();
            if (trimmed.StartsWith("```"))
            {
                var lines = trimmed.Split('\n');
                trimmed = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
            }

            var topics = JsonSerializer.Deserialize<List<TopicMention>>(trimmed,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // Dedup by name, take max 3
            var deduped = topics?
                .GroupBy(t => t.Topic?.ToLowerInvariant() ?? "")
                .Select(g => g.First())
                .Take(3)
                .ToList() ?? [];

            _logger.LogDebug("Extracted {Count} topics", deduped.Count);
            return deduped;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Topic extraction failed");
            return [];
        }
    }

    /// <summary>Store a topic mention in the vector store.</summary>
    public async Task StoreMentionAsync(
        TopicMention topic, string userId, float[]? embedding = null, CancellationToken ct = default)
    {
        var dataText = $"Topic: {topic.Topic} ({topic.TopicType}) - {topic.ContextSnippet}";

        var payload = new Dictionary<string, object?>
        {
            ["data"] = dataText,
            ["user_id"] = userId,
            ["memory_type"] = "topic_mention",
            ["topic_name"] = topic.Topic,
            ["topic_type"] = topic.TopicType,
            ["emotional_weight"] = topic.EmotionalWeight,
            ["created_at"] = DateTime.UtcNow.ToString("o"),
        };

        // Generate embedding if not provided
        embedding ??= await _embeddingClient.EmbedAsync(dataText, ct);

        var id = Guid.NewGuid().ToString();
        await _vectorStore.InsertAsync(id, embedding, payload, ct);
        _logger.LogDebug("Stored topic mention: {Topic} (id={Id})", topic.Topic, id);
    }

    /// <summary>Retrieve recurring topics (topics with >= 2 mentions in last 14 days).</summary>
    public async Task<List<string>> GetRecurringTopicsAsync(
        string userId, int maxTopics = 3, CancellationToken ct = default)
    {
        try
        {
            var items = await _vectorStore.GetAllAsync(
                new Dictionary<string, object?>
                {
                    ["user_id"] = userId,
                    ["memory_type"] = "topic_mention",
                },
                limit: 100, ct: ct);

            // 14-day lookback filter
            var cutoff = DateTime.UtcNow.AddDays(-14);
            var recent = items.Where(i =>
            {
                var createdStr = i.Metadata.GetValueOrDefault("created_at")?.ToString();
                return createdStr is null || !DateTime.TryParse(createdStr, out var created) || created >= cutoff;
            }).ToList();

            // Group by topic name, filter >= 2 mentions
            var recurring = recent
                .GroupBy(i => i.Metadata.GetValueOrDefault("topic_name")?.ToString() ?? "")
                .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() >= 2)
                .Take(maxTopics)
                .Select(g =>
                {
                    var count = g.Count();
                    var weights = g.Select(i => i.Metadata.GetValueOrDefault("emotional_weight")?.ToString() ?? "light");
                    var avgWeight = weights.GroupBy(w => w).OrderByDescending(wg => wg.Count()).First().Key;
                    return $"{g.Key}: mentioned {count} times (emotional weight: {avgWeight})";
                })
                .ToList();

            return recurring;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve recurring topics");
            return [];
        }
    }
}

public sealed class TopicMention
{
    public string Topic { get; set; } = "";
    public string TopicType { get; set; } = "theme"; // "entity" or "theme"
    public string ContextSnippet { get; set; } = "";
    public string EmotionalWeight { get; set; } = "light"; // "light", "moderate", "heavy"
}
