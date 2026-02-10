using System.Text.RegularExpressions;
using Clara.Core.Llm;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Memory.Extraction;

public enum ContradictionType { None, Negation, Antonym, Temporal, Numeric, Semantic }

public sealed record ContradictionResult(
    bool Contradicts,
    ContradictionType Type = ContradictionType.None,
    double Confidence = 0.0,
    string Explanation = "");

/// <summary>
/// 5-layer fast-to-slow contradiction detection.
/// Port of clara_core/contradiction.py.
/// </summary>
public sealed class ContradictionDetector
{
    private readonly ILlmProvider? _llm;
    private readonly ILogger<ContradictionDetector> _logger;

    // Negation pattern pairs: (positive, negative)
    private static readonly (string Positive, string Negative)[] NegationPatterns =
    [
        (@"\b(is|am|are|was|were)\b", @"\b(is|am|are|was|were)\s+(not|n't)\b"),
        (@"\b(do|does|did)\b", @"\b(do|does|did)\s+(not|n't)\b"),
        (@"\b(has|have|had)\b", @"\b(has|have|had)\s+(not|n't)\b"),
        (@"\b(can|could|will|would|should|might)\b", @"\b(can|could|will|would|should|might)\s+(not|n't)\b"),
        (@"\blikes?\b", @"\b(doesn't|does not|don't|do not)\s+like\b"),
        (@"\bloves?\b", @"\b(doesn't|does not|don't|do not)\s+love\b"),
        (@"\bwants?\b", @"\b(doesn't|does not|don't|do not)\s+want\b"),
        (@"\bprefers?\b", @"\b(doesn't|does not|don't|do not)\s+prefer\b"),
    ];

    // Antonym pairs
    private static readonly (string A, string B)[] AntonymPairs =
    [
        ("available", "busy"), ("available", "unavailable"), ("happy", "sad"),
        ("happy", "unhappy"), ("good", "bad"), ("like", "dislike"), ("like", "hate"),
        ("love", "hate"), ("agree", "disagree"), ("want", "avoid"), ("prefer", "dislike"),
        ("enjoy", "dislike"), ("enjoy", "hate"), ("interested", "uninterested"),
        ("interested", "bored"), ("yes", "no"), ("true", "false"), ("correct", "incorrect"),
        ("right", "wrong"), ("active", "inactive"), ("enabled", "disabled"),
        ("open", "closed"), ("alive", "dead"), ("married", "single"),
        ("married", "divorced"), ("employed", "unemployed"), ("working", "retired"),
    ];

    private static readonly string[] StopWords =
        ["the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
         "have", "has", "had", "do", "does", "did", "will", "would", "could",
         "should", "may", "might", "shall", "can", "to", "of", "in", "for",
         "on", "with", "at", "by", "from", "it", "that", "this", "and", "or",
         "not", "no", "but", "if", "so", "as", "they", "them", "their", "he",
         "she", "his", "her", "my", "your", "our", "i", "you", "we"];

    private static readonly Regex DatePattern = new(
        @"\b(\d{1,2})[/-](\d{1,2})[/-](\d{2,4})\b|\b(\d{4})[/-](\d{1,2})[/-](\d{1,2})\b",
        RegexOptions.Compiled);

    private static readonly Regex NumericPattern = new(
        @"\b(\d+(?:\.\d+)?)\s*(years?|months?|weeks?|days?|hours?|dollars?|%)?\b",
        RegexOptions.Compiled);

    public ContradictionDetector(ILogger<ContradictionDetector> logger, ILlmProvider? llm = null)
    {
        _logger = logger;
        _llm = llm;
    }

    /// <summary>Detect if new content contradicts existing content.</summary>
    public async Task<ContradictionResult> DetectAsync(
        string newContent, string existingContent, bool useLlm = false, CancellationToken ct = default)
    {
        var newLower = newContent.ToLowerInvariant().Trim();
        var existingLower = existingContent.ToLowerInvariant().Trim();

        if (newLower == existingLower)
            return new ContradictionResult(false);

        // Layer 1: Negation patterns (confidence 0.8)
        var result = CheckNegation(newLower, existingLower);
        if (result.Contradicts) return result;

        // Layer 2: Antonym detection (confidence 0.7)
        result = CheckAntonyms(newLower, existingLower);
        if (result.Contradicts) return result;

        // Layer 3: Temporal conflicts (confidence 0.6)
        result = CheckTemporal(newLower, existingLower);
        if (result.Contradicts) return result;

        // Layer 4: Numeric conflicts (confidence 0.65)
        result = CheckNumeric(newLower, existingLower);
        if (result.Contradicts) return result;

        // Layer 5: LLM semantic check (confidence 0.85, optional)
        if (useLlm && _llm is not null)
        {
            result = await CheckSemanticAsync(newContent, existingContent, ct);
            if (result.Contradicts) return result;
        }

        return new ContradictionResult(false);
    }

    private static ContradictionResult CheckNegation(string newText, string existingText)
    {
        foreach (var (positive, negative) in NegationPatterns)
        {
            var posInNew = Regex.IsMatch(newText, positive);
            var negInNew = Regex.IsMatch(newText, negative);
            var posInExisting = Regex.IsMatch(existingText, positive);
            var negInExisting = Regex.IsMatch(existingText, negative);

            if ((posInNew && negInExisting) || (negInNew && posInExisting))
            {
                // Check common context
                if (HasCommonContext(newText, existingText))
                {
                    return new ContradictionResult(true, ContradictionType.Negation, 0.8,
                        "Negation pattern detected between statements");
                }
            }
        }

        return new ContradictionResult(false);
    }

    private static ContradictionResult CheckAntonyms(string newText, string existingText)
    {
        foreach (var (a, b) in AntonymPairs)
        {
            var aInNew = newText.Contains(a);
            var bInNew = newText.Contains(b);
            var aInExisting = existingText.Contains(a);
            var bInExisting = existingText.Contains(b);

            if ((aInNew && bInExisting) || (bInNew && aInExisting))
            {
                if (HasCommonContext(newText, existingText))
                {
                    return new ContradictionResult(true, ContradictionType.Antonym, 0.7,
                        $"Antonym pair detected: '{a}' vs '{b}'");
                }
            }
        }

        return new ContradictionResult(false);
    }

    private static ContradictionResult CheckTemporal(string newText, string existingText)
    {
        var newDates = DatePattern.Matches(newText);
        var existingDates = DatePattern.Matches(existingText);

        if (newDates.Count > 0 && existingDates.Count > 0)
        {
            var newDateStrings = newDates.Select(m => m.Value).ToHashSet();
            var existingDateStrings = existingDates.Select(m => m.Value).ToHashSet();

            if (!newDateStrings.Overlaps(existingDateStrings) && HasCommonContext(newText, existingText))
            {
                return new ContradictionResult(true, ContradictionType.Temporal, 0.6,
                    "Different dates detected for similar context");
            }
        }

        return new ContradictionResult(false);
    }

    private static ContradictionResult CheckNumeric(string newText, string existingText)
    {
        var newNums = NumericPattern.Matches(newText);
        var existingNums = NumericPattern.Matches(existingText);

        if (newNums.Count > 0 && existingNums.Count > 0)
        {
            foreach (Match newNum in newNums)
            {
                foreach (Match existingNum in existingNums)
                {
                    var newUnit = newNum.Groups[2].Value;
                    var existingUnit = existingNum.Groups[2].Value;

                    // Same unit type but different values
                    if (!string.IsNullOrEmpty(newUnit) && newUnit == existingUnit
                        && newNum.Groups[1].Value != existingNum.Groups[1].Value
                        && HasCommonContext(newText, existingText))
                    {
                        return new ContradictionResult(true, ContradictionType.Numeric, 0.65,
                            $"Different numeric values: {newNum.Value} vs {existingNum.Value}");
                    }
                }
            }
        }

        return new ContradictionResult(false);
    }

    private async Task<ContradictionResult> CheckSemanticAsync(
        string newContent, string existingContent, CancellationToken ct)
    {
        var prompt = $"""
            Determine if these two statements contradict each other.

            Statement A: {newContent}
            Statement B: {existingContent}

            Reply with exactly one word: CONTRADICT, NO_CONTRADICTION, or UPDATES
            """;

        try
        {
            var response = await _llm!.CompleteAsync(
                [new SystemMessage("You are a contradiction detector. Reply with one word only."),
                 new UserMessage(prompt)],
                ct: ct);

            var answer = response.Trim().ToUpperInvariant();
            if (answer.Contains("CONTRADICT") && !answer.Contains("NO_CONTRADICTION"))
            {
                return new ContradictionResult(true, ContradictionType.Semantic, 0.85,
                    "LLM semantic analysis detected contradiction");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM contradiction check failed, skipping semantic layer");
        }

        return new ContradictionResult(false);
    }

    private static bool HasCommonContext(string a, string b)
    {
        var stopSet = new HashSet<string>(StopWords);
        var wordsA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopSet.Contains(w)).ToHashSet();
        var wordsB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopSet.Contains(w)).ToHashSet();

        var common = wordsA.Intersect(wordsB).Count();
        return common >= 1;
    }

    /// <summary>Fast Jaccard word-overlap similarity (0-1).</summary>
    public static double CalculateSimilarity(string a, string b)
    {
        var wordsA = a.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var wordsB = b.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (wordsA.Count == 0 && wordsB.Count == 0) return 1.0;
        if (wordsA.Count == 0 || wordsB.Count == 0) return 0.0;

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();
        return (double)intersection / union;
    }
}
