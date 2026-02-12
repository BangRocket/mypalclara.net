using System.Text.Json;
using MyPalClara.Core.Llm;
using MyPalClara.Memory.Prompts;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory.Extraction;

/// <summary>
/// LLM-based fact extraction from conversations using mem0-style prompts.
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
        var conversation = $"User: {userMessage}\nAssistant: {assistantResponse}";

        try
        {
            var response = await _rook.CompleteAsync(
                [new SystemMessage(MemoryPrompts.UserFactExtractionPrompt),
                 new UserMessage(conversation)],
                ct: ct);

            // Parse JSON {"facts": [...]} from response
            var trimmed = response.Trim();

            // Handle markdown code blocks
            if (trimmed.StartsWith("```"))
            {
                var lines = trimmed.Split('\n');
                trimmed = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
            }

            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (root.TryGetProperty("facts", out var factsArray))
            {
                var facts = factsArray.EnumerateArray()
                    .Select(f => f.GetString())
                    .Where(f => !string.IsNullOrEmpty(f))
                    .Select(f => f!)
                    .ToList();

                _logger.LogDebug("Extracted {Count} facts", facts.Count);
                return facts;
            }

            _logger.LogDebug("No 'facts' key in extraction response");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fact extraction failed");
            return [];
        }
    }
}
