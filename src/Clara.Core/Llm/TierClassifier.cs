namespace Clara.Core.Llm;

/// <summary>
/// Simple heuristic-based tier classifier for routing messages to appropriate model tiers.
/// </summary>
public static class TierClassifier
{
    private static readonly string[] CodeIndicators =
    [
        "```", "def ", "class ", "function ", "public ", "private ",
        "import ", "#include", "package ", "namespace ", "var ", "const ",
        "async ", "await ", "return ", "if (", "for (", "while ("
    ];

    /// <summary>
    /// Classify a message to determine which model tier to use.
    /// </summary>
    public static ModelTier Classify(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return ModelTier.Low;

        // Short messages -> Low tier
        if (message.Length < 50)
            return ModelTier.Low;

        // Messages with code blocks or code indicators -> High tier
        if (HasCodeContent(message))
            return ModelTier.High;

        // Default -> Mid tier
        return ModelTier.Mid;
    }

    private static bool HasCodeContent(string message)
    {
        foreach (var indicator in CodeIndicators)
        {
            if (message.Contains(indicator, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
