using System.Text.Json;
using Clara.Core.Llm;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Memory.Extraction;

/// <summary>
/// LLM-based fact extraction from conversations.
/// Uses the Rook provider (OpenAI-compatible) for extraction.
/// </summary>
public sealed class FactExtractor
{
    private readonly RookProvider _rook;
    private readonly ILogger<FactExtractor> _logger;

    public FactExtractor(RookProvider rook, ILogger<FactExtractor> logger)
    {
        _rook = rook;
        _logger = logger;
    }

    /// <summary>Extract discrete facts from a conversation exchange.</summary>
    public async Task<List<string>> ExtractFactsAsync(
        string userMessage, string assistantResponse, CancellationToken ct = default)
    {
        var prompt = $"""
            Extract discrete, important facts about the user from this conversation.
            Only extract facts that are personal, preferential, or otherwise worth remembering.
            Return a JSON array of strings, each a standalone fact.
            If no memorable facts, return an empty array [].

            User: {userMessage}
            Assistant: {assistantResponse}

            Return ONLY a JSON array, no other text.
            """;

        try
        {
            var response = await _rook.CompleteAsync(
                [new SystemMessage("You extract facts from conversations. Return only JSON arrays."),
                 new UserMessage(prompt)],
                ct: ct);

            // Parse JSON array from response
            var trimmed = response.Trim();

            // Handle markdown code blocks
            if (trimmed.StartsWith("```"))
            {
                var lines = trimmed.Split('\n');
                trimmed = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
            }

            var facts = JsonSerializer.Deserialize<List<string>>(trimmed);
            _logger.LogDebug("Extracted {Count} facts", facts?.Count ?? 0);
            return facts ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fact extraction failed");
            return [];
        }
    }
}
