using System.Text.Json;
using MyPalClara.Core.Llm;
using MyPalClara.Core.Memory;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory.Context;

/// <summary>
/// Topic extraction and recurrence pattern detection.
/// Uses LLM-based extraction + FalkorDB Memory nodes for persistence.
/// </summary>
public sealed class TopicRecurrence
{
    private readonly RookProvider _rook;
    private readonly ISemanticMemoryStore _store;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<TopicRecurrence> _logger;

    public TopicRecurrence(
        RookProvider rook,
        ISemanticMemoryStore store,
        EmbeddingClient embeddingClient,
        ILogger<TopicRecurrence> logger)
    {
        _rook = rook;
        _store = store;
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

    /// <summary>Store a topic mention as a Memory node.</summary>
    public async Task StoreMentionAsync(
        TopicMention topic, string userId, float[]? embedding = null, CancellationToken ct = default)
    {
        var dataText = $"Topic: {topic.Topic} ({topic.TopicType}) - {topic.ContextSnippet}";

        embedding ??= await _embeddingClient.EmbedAsync(dataText, ct);

        var id = Guid.NewGuid().ToString();
        await _store.InsertMemoryAsync(id, embedding, dataText, userId,
            new Dictionary<string, object?>
            {
                ["memory_type"] = "topic_mention",
                ["topic_name"] = topic.Topic,
                ["topic_type"] = topic.TopicType,
                ["emotional_weight"] = topic.EmotionalWeight,
            }, ct);

        _logger.LogDebug("Stored topic mention: {Topic} (id={Id})", topic.Topic, id);
    }

    /// <summary>Retrieve recurring topics across linked user IDs (topics with >= 2 mentions in last 14 days).</summary>
    public async Task<List<string>> GetRecurringTopicsAsync(
        IReadOnlyList<string> userIds, int maxTopics = 3, CancellationToken ct = default)
    {
        try
        {
            var items = await _store.GetAllMemoriesAsync(
                userIds,
                new Dictionary<string, object?> { ["memory_type"] = "topic_mention" },
                limit: 100, ct: ct);

            var cutoff = DateTime.UtcNow.AddDays(-14);
            var recent = items.Where(i =>
            {
                var createdStr = i.Metadata.GetValueOrDefault("created_at")?.ToString();
                return createdStr is null || !DateTime.TryParse(createdStr, out var created) || created >= cutoff;
            }).ToList();

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
    public string TopicType { get; set; } = "theme";
    public string ContextSnippet { get; set; } = "";
    public string EmotionalWeight { get; set; } = "light";
}
