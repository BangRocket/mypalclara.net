namespace Clara.Core.Memory.Extraction;

/// <summary>
/// Keyword-based memory classification.
/// Port of memory_manager.py lines 949-979.
/// </summary>
public static class CategoryClassifier
{
    private static readonly Dictionary<string, string[]> CategoryKeywords = new()
    {
        ["preferences"] = ["prefer", "like", "favorite", "love", "hate", "dislike", "enjoy", "want"],
        ["personal"] = ["my name", "i am", "i'm from", "my family", "my wife", "birthday"],
        ["professional"] = ["work", "job", "career", "company", "project", "team", "meeting"],
        ["goals"] = ["want to", "plan to", "going to", "hope to", "goal", "dream", "aspire"],
        ["emotional"] = ["feel", "feeling", "mood", "happy", "sad", "anxious", "excited", "stressed"],
        ["temporal"] = ["yesterday", "today", "tomorrow", "last week", "next week", "recently"],
    };

    /// <summary>Classify a memory text into a category using keyword matching.</summary>
    public static string? Classify(string text)
    {
        var lower = text.ToLowerInvariant();
        string? bestCategory = null;
        int bestScore = 0;

        foreach (var (category, keywords) in CategoryKeywords)
        {
            var score = keywords.Count(k => lower.Contains(k));
            if (score > bestScore)
            {
                bestScore = score;
                bestCategory = category;
            }
        }

        return bestCategory;
    }
}
