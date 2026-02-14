using MyPalClara.Core.Configuration;
using MyPalClara.Core.Llm;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.Sessions;

/// <summary>
/// Compacts conversation history when token count exceeds threshold.
/// Summarizes older messages into a single system message while keeping recent messages intact.
/// </summary>
public sealed class SessionCompactor
{
    private readonly ILlmProvider _llm;
    private readonly ClaraConfig _config;
    private readonly ILogger<SessionCompactor> _logger;

    public SessionCompactor(ILlmProvider llm, ClaraConfig config, ILogger<SessionCompactor> logger)
    {
        _llm = llm;
        _config = config;
        _logger = logger;
    }

    public async Task<List<ChatMessage>> CompactIfNeededAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        var gw = _config.Gateway;
        var totalTokens = TokenEstimator.EstimateMessages(messages);

        if (totalTokens <= gw.CompactionThresholdTokens)
            return messages;

        _logger.LogInformation(
            "Session compaction triggered: {Tokens} tokens exceeds threshold {Threshold}",
            totalTokens, gw.CompactionThresholdTokens);

        // Find how many recent messages to keep (counting from the end)
        var keepTokens = 0;
        var keepFrom = messages.Count;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msgTokens = TokenEstimator.Estimate(messages[i].Content) + 4;
            if (keepTokens + msgTokens > gw.CompactionTargetTokens)
                break;
            keepTokens += msgTokens;
            keepFrom = i;
        }

        // If we'd keep everything, no compaction needed
        if (keepFrom <= 1)
            return messages;

        // Summarize the older messages
        var toSummarize = messages[..keepFrom];
        var summary = await SummarizeAsync(toSummarize, ct);

        var compacted = new List<ChatMessage>
        {
            new SystemMessage($"[Previous conversation summary]\n{summary}"),
        };
        compacted.AddRange(messages[keepFrom..]);

        _logger.LogInformation(
            "Compacted {OldCount} messages into summary + {KeptCount} recent messages ({OldTokens} → {NewTokens} tokens)",
            messages.Count, messages.Count - keepFrom,
            totalTokens, TokenEstimator.EstimateMessages(compacted));

        return compacted;
    }

    private async Task<string> SummarizeAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        var transcript = string.Join("\n", messages.Select(m => $"[{m.Role}]: {m.Content}"));

        var summarizeMessages = new List<ChatMessage>
        {
            new SystemMessage("Summarize this conversation concisely. Preserve key facts, decisions, and context needed for continuity. Be brief but complete."),
            new UserMessage(transcript),
        };

        var model = _config.Llm.ModelForTier("low");
        return await _llm.CompleteAsync(summarizeMessages, model, maxTokens: 2000, ct: ct);
    }
}
