using Clara.Core.Memory.Vector;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Memory.Context;

/// <summary>
/// Sentiment tracking and emotional arc computation.
/// Port of clara_core/emotional_context.py.
/// Uses simple lexicon-based VADER-style sentiment analysis.
/// </summary>
public sealed class EmotionalContext
{
    private readonly IVectorStore _vectorStore;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<EmotionalContext> _logger;

    // Per-(user, channel) sentiment history
    private readonly Dictionary<string, List<SentimentEntry>> _sessions = new();

    // Simplified VADER-style lexicon (positive and negative word scores)
    private static readonly Dictionary<string, float> Lexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        // Positive
        ["happy"] = 2.7f, ["love"] = 3.2f, ["great"] = 2.5f, ["good"] = 1.9f,
        ["awesome"] = 3.1f, ["amazing"] = 3.1f, ["wonderful"] = 2.8f, ["excellent"] = 2.7f,
        ["fantastic"] = 3.0f, ["beautiful"] = 2.7f, ["enjoy"] = 2.0f, ["fun"] = 2.1f,
        ["exciting"] = 2.5f, ["glad"] = 2.0f, ["pleased"] = 2.0f, ["thankful"] = 2.2f,
        ["grateful"] = 2.4f, ["proud"] = 2.2f, ["confident"] = 2.0f, ["hopeful"] = 1.8f,
        ["better"] = 1.5f, ["best"] = 2.5f, ["nice"] = 1.8f, ["cool"] = 1.5f,

        // Negative
        ["sad"] = -2.1f, ["hate"] = -3.0f, ["bad"] = -2.1f, ["terrible"] = -2.8f,
        ["awful"] = -2.7f, ["horrible"] = -2.7f, ["worst"] = -3.1f, ["angry"] = -2.5f,
        ["upset"] = -2.1f, ["frustrated"] = -2.3f, ["anxious"] = -1.8f, ["stressed"] = -2.0f,
        ["worried"] = -1.8f, ["afraid"] = -2.0f, ["disappointed"] = -2.2f, ["lonely"] = -2.1f,
        ["depressed"] = -2.5f, ["miserable"] = -2.8f, ["exhausted"] = -1.5f, ["overwhelmed"] = -1.8f,
        ["annoyed"] = -1.8f, ["hurt"] = -2.1f, ["scared"] = -2.0f, ["struggling"] = -1.8f,
        ["sucks"] = -2.0f, ["shit"] = -1.5f, ["damn"] = -1.0f, ["fuck"] = -1.5f,
    };

    public EmotionalContext(IVectorStore vectorStore, EmbeddingClient embeddingClient, ILogger<EmotionalContext> logger)
    {
        _vectorStore = vectorStore;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    /// <summary>Analyze and track sentiment for a message.</summary>
    public void TrackMessage(string userId, string channelId, string message)
    {
        var key = $"{userId}:{channelId}";
        if (!_sessions.TryGetValue(key, out var entries))
        {
            entries = [];
            _sessions[key] = entries;
        }

        var score = AnalyzeSentiment(message);
        entries.Add(new SentimentEntry(score, DateTime.UtcNow));
    }

    /// <summary>Simple lexicon-based sentiment analysis. Returns compound score -1 to +1.</summary>
    public static float AnalyzeSentiment(string text)
    {
        var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float total = 0;
        int count = 0;

        foreach (var word in words)
        {
            // Strip basic punctuation
            var clean = word.TrimEnd('.', ',', '!', '?', ';', ':');
            if (Lexicon.TryGetValue(clean, out var score))
            {
                total += score;
                count++;
            }
        }

        if (count == 0) return 0f;

        // Normalize to -1..+1 range (VADER-style compound)
        var raw = total / count;
        return raw / (float)Math.Sqrt(raw * raw + 15); // Normalization factor
    }

    /// <summary>Compute emotional arc for a session. Requires >= 3 messages.</summary>
    public string? ComputeArc(string userId, string channelId)
    {
        var key = $"{userId}:{channelId}";
        if (!_sessions.TryGetValue(key, out var entries) || entries.Count < 3)
            return null;

        var startAvg = entries.Take(3).Average(e => e.Score);
        var endAvg = entries.TakeLast(3).Average(e => e.Score);
        var variance = entries.Select(e => (double)e.Score).ToArray();
        var mean = variance.Average();
        var v = variance.Average(x => (x - mean) * (x - mean));

        string arc;
        if (v > 0.3) arc = "volatile";
        else if (endAvg - startAvg > 0.2) arc = "improving";
        else if (startAvg - endAvg > 0.2) arc = "declining";
        else arc = "stable";

        var energy = endAvg switch
        {
            > 0.2f => "positive",
            < -0.2f => "negative",
            _ => "neutral",
        };

        return $"Emotional arc was {arc} throughout. Ended with {energy} energy.";
    }

    /// <summary>Finalize and store emotional context for a session (on idle or session end).</summary>
    public async Task FinalizeSessionAsync(string userId, string channelId, string? topic = null, CancellationToken ct = default)
    {
        var arc = ComputeArc(userId, channelId);
        if (arc is null) return;

        var topicPart = !string.IsNullOrEmpty(topic) ? $"about {topic}" : "";
        var memory = $"Conversation {topicPart}. They {arc}";

        var key = $"{userId}:{channelId}";
        var entries = _sessions.GetValueOrDefault(key);
        var endAvg = entries?.TakeLast(3).Average(e => e.Score) ?? 0;

        var payload = new Dictionary<string, object?>
        {
            ["data"] = memory,
            ["user_id"] = userId,
            ["memory_type"] = "emotional_context",
            ["channel_id"] = channelId,
            ["sentiment_end"] = endAvg,
            ["created_at"] = DateTime.UtcNow.ToString("o"),
        };

        // Generate embedding and store
        var embedding = await _embeddingClient.EmbedAsync(memory, ct);
        var id = Guid.NewGuid().ToString();
        await _vectorStore.InsertAsync(id, embedding, payload, ct);
        _logger.LogDebug("Finalized emotional context: {Arc} (id={Id})", arc, id);

        // Clear session
        _sessions.Remove(key);
    }

    /// <summary>Retrieve recent emotional context from vector store (across all linked user IDs).</summary>
    public async Task<List<string>> RetrieveAsync(IReadOnlyList<string> userIds, int maxItems = 3, CancellationToken ct = default)
    {
        try
        {
            var items = await _vectorStore.GetAllAsync(
                new Dictionary<string, object?>
                {
                    ["user_id"] = userIds.Count == 1 ? userIds[0] : (object)userIds,
                    ["memory_type"] = "emotional_context",
                },
                limit: maxItems, ct: ct);

            return items
                .Select(i => i.Memory)
                .Where(m => !string.IsNullOrEmpty(m))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve emotional context");
            return [];
        }
    }

    private sealed record SentimentEntry(float Score, DateTime Timestamp);
}
