using System.Text.Json;
using MyPalClara.Core.Llm;
using MyPalClara.Core.Memory;
using MyPalClara.Memory.History;
using MyPalClara.Memory.Prompts;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory.Extraction;

/// <summary>
/// Core mem0-style pipeline replacing SmartIngest.
/// For each set of extracted facts:
///   1. Embed each fact
///   2. Vector-search for similar existing memories
///   3. One LLM call to decide ADD/UPDATE/DELETE/NONE per fact
///   4. Execute operations + record history
/// </summary>
public sealed class MemoryManager
{
    private readonly ISemanticMemoryStore _store;
    private readonly EmbeddingClient _embeddingClient;
    private readonly RookProvider _rook;
    private readonly MemoryHistoryStore _historyStore;
    private readonly ILogger<MemoryManager> _logger;

    public MemoryManager(
        ISemanticMemoryStore store,
        EmbeddingClient embeddingClient,
        RookProvider rook,
        MemoryHistoryStore historyStore,
        ILogger<MemoryManager> logger)
    {
        _store = store;
        _embeddingClient = embeddingClient;
        _rook = rook;
        _historyStore = historyStore;
        _logger = logger;
    }

    private const double ScoreThreshold = 0.1;
    private static readonly HashSet<string> NonFactCategories = ["topic_mention", "emotional_context"];

    /// <summary>
    /// Process extracted facts through the mem0-style pipeline.
    /// Returns the list of actions taken.
    /// </summary>
    public async Task<List<MemoryAction>> ProcessFactsAsync(
        List<string> facts, string userId,
        Dictionary<string, object?>? metadata = null,
        CancellationToken ct = default)
    {
        if (facts.Count == 0) return [];

        // 1. Embed all facts in a single batch API call
        var embeddings = await _embeddingClient.EmbedBatchAsync(facts, ct);

        // 2. Vector-search for similar existing memories per fact (parallel)
        var searchTasks = embeddings.Select((emb, _) =>
            _store.SearchAsync(emb, [userId], limit: 5, ct: ct));
        var searchResults = await Task.WhenAll(searchTasks);

        // 3. Deduplicate results, apply score threshold, filter non-fact memories
        var allFound = new Dictionary<string, (string Text, double Score)>();
        foreach (var similar in searchResults)
        {
            foreach (var item in similar)
            {
                if (item.Score < ScoreThreshold) continue;

                // Skip non-fact memory types (topic mentions, emotional context)
                var cat = item.Metadata.GetValueOrDefault("category")?.ToString();
                if (cat is not null && NonFactCategories.Contains(cat)) continue;

                if (!allFound.ContainsKey(item.Id) || item.Score > allFound[item.Id].Score)
                    allFound[item.Id] = (item.Memory, item.Score);
            }
        }

        // 4. Map real UUIDs to sequential integers (prevent LLM hallucination)
        var idToIndex = new Dictionary<string, string>();
        var indexToId = new Dictionary<string, string>();
        var existingMemories = new List<(string Id, string Text)>();

        int idx = 0;
        foreach (var (realId, (text, _)) in allFound)
        {
            var indexStr = idx.ToString();
            idToIndex[realId] = indexStr;
            indexToId[indexStr] = realId;
            existingMemories.Add((indexStr, text));
            idx++;
        }

        // 5. One LLM call: BuildUpdateMemoryPrompt with all old memories + all new facts
        var prompt = MemoryPrompts.BuildUpdateMemoryPrompt(existingMemories, facts);

        string llmResponse;
        try
        {
            llmResponse = await _rook.CompleteAsync(
                [new SystemMessage("You are a memory manager. Return only JSON."),
                 new UserMessage(prompt)],
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM update memory call failed");
            return [];
        }

        // 6. Parse JSON response
        var actions = ParseMemoryActions(llmResponse);
        if (actions.Count == 0)
        {
            _logger.LogDebug("No memory actions returned from LLM");
            return [];
        }

        // 7. Execute per action, mapping integers back to real UUIDs
        var results = new List<MemoryAction>();
        foreach (var action in actions)
        {
            try
            {
                switch (action.Event.ToUpperInvariant())
                {
                    case "ADD":
                        var newId = Guid.NewGuid().ToString();
                        var category = CategoryClassifier.Classify(action.Text);
                        var addEmbedding = await _embeddingClient.EmbedAsync(action.Text, ct);

                        var mergedMetadata = new Dictionary<string, object?>();
                        if (metadata is not null)
                            foreach (var kv in metadata)
                                mergedMetadata[kv.Key] = kv.Value;
                        if (category is not null)
                            mergedMetadata["category"] = category;

                        await _store.InsertMemoryAsync(newId, addEmbedding, action.Text, userId, mergedMetadata, ct);
                        await _historyStore.AddEntryAsync(newId, null, action.Text, "ADD", userId, ct);

                        results.Add(new MemoryAction("ADD", newId, action.Text));
                        _logger.LogDebug("ADD memory {Id}: {Text}", newId, Truncate(action.Text));
                        break;

                    case "UPDATE":
                        var realId = indexToId.GetValueOrDefault(action.Id, action.Id);
                        var updateEmbedding = await _embeddingClient.EmbedAsync(action.Text, ct);

                        await _store.UpdateMemoryAsync(realId, updateEmbedding, action.Text, ct: ct);
                        await _historyStore.AddEntryAsync(realId, action.OldMemory, action.Text, "UPDATE", userId, ct);

                        results.Add(new MemoryAction("UPDATE", realId, action.Text, action.OldMemory));
                        _logger.LogDebug("UPDATE memory {Id}: {Old} -> {New}",
                            realId, Truncate(action.OldMemory ?? ""), Truncate(action.Text));
                        break;

                    case "DELETE":
                        var deleteId = indexToId.GetValueOrDefault(action.Id, action.Id);

                        // Get existing text for history before deleting
                        var existing = allFound.GetValueOrDefault(
                            indexToId.ContainsKey(action.Id) ? indexToId[action.Id] : action.Id);
                        var oldText = existing.Text ?? action.Text;

                        await _store.DeleteMemoryAsync(deleteId, ct);
                        await _historyStore.AddEntryAsync(deleteId, oldText, null, "DELETE", userId, ct);

                        results.Add(new MemoryAction("DELETE", deleteId, action.Text));
                        _logger.LogDebug("DELETE memory {Id}", deleteId);
                        break;

                    case "NONE":
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to execute memory action {Event} for {Id}",
                    action.Event, action.Id);
            }
        }

        _logger.LogInformation("Memory pipeline: {Count} actions executed ({Details})",
            results.Count,
            string.Join(", ", results.GroupBy(r => r.Event).Select(g => $"{g.Key}={g.Count()}")));

        return results;
    }

    private List<LlmMemoryAction> ParseMemoryActions(string response)
    {
        try
        {
            var trimmed = response.Trim();

            // Handle markdown code blocks
            if (trimmed.StartsWith("```"))
            {
                var lines = trimmed.Split('\n');
                trimmed = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
            }

            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (!root.TryGetProperty("memory", out var memoryArray))
                return [];

            var actions = new List<LlmMemoryAction>();
            foreach (var item in memoryArray.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                var text = item.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
                var evt = item.TryGetProperty("event", out var eventProp) ? eventProp.GetString() ?? "NONE" : "NONE";
                var oldMemory = item.TryGetProperty("old_memory", out var oldProp) ? oldProp.GetString() : null;

                actions.Add(new LlmMemoryAction(id, text, evt, oldMemory));
            }

            return actions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM memory response");
            return [];
        }
    }

    private static string Truncate(string s, int maxLen = 80) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";

    private sealed record LlmMemoryAction(string Id, string Text, string Event, string? OldMemory);
}

public sealed record MemoryAction(string Event, string MemoryId, string Text, string? OldMemory = null);
