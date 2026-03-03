using Clara.Core.Config;

namespace Clara.Core.Tools.ToolPolicy;

/// <summary>
/// Per-agent allow/deny list from configuration.
/// Priority 100 (evaluated early).
/// </summary>
public class AgentToolPolicy : IToolPolicy
{
    private readonly ToolPolicyOptions _options;

    public AgentToolPolicy(ToolPolicyOptions options) => _options = options;

    public int Priority => 100;

    public ToolPolicyDecision Evaluate(string toolName, ToolExecutionContext context)
    {
        // Deny list takes precedence
        if (_options.Deny.Any(pattern => MatchesPattern(toolName, pattern)))
            return ToolPolicyDecision.Deny;

        // Allow list
        if (_options.Allow.Any(pattern => MatchesPattern(toolName, pattern)))
            return ToolPolicyDecision.Allow;

        return ToolPolicyDecision.Abstain;
    }

    private static bool MatchesPattern(string toolName, string pattern)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith('*'))
            return toolName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return string.Equals(toolName, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
