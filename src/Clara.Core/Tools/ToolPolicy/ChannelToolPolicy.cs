using Clara.Core.Config;

namespace Clara.Core.Tools.ToolPolicy;

/// <summary>
/// Per-channel restrictions from configuration.
/// Priority 200 (evaluated after agent policy).
/// Channel patterns support wildcards, e.g. "discord:*" or "slack:channel:general".
/// </summary>
public class ChannelToolPolicy : IToolPolicy
{
    private readonly Dictionary<string, ToolPolicyOptions> _channelPolicies;

    public ChannelToolPolicy(Dictionary<string, ToolPolicyOptions> channelPolicies) =>
        _channelPolicies = channelPolicies;

    public int Priority => 200;

    public ToolPolicyDecision Evaluate(string toolName, ToolExecutionContext context)
    {
        var channelKey = $"{context.Platform}:{context.SessionKey}";

        foreach (var (pattern, policy) in _channelPolicies)
        {
            if (!MatchesChannelPattern(channelKey, pattern))
                continue;

            // Check deny first
            if (policy.Deny.Any(p => MatchesToolPattern(toolName, p)))
                return ToolPolicyDecision.Deny;

            // Check allow
            if (policy.Allow.Any(p => MatchesToolPattern(toolName, p)))
                return ToolPolicyDecision.Allow;
        }

        return ToolPolicyDecision.Abstain;
    }

    private static bool MatchesChannelPattern(string channelKey, string pattern)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith('*'))
            return channelKey.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return string.Equals(channelKey, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesToolPattern(string toolName, string pattern)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith('*'))
            return toolName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return string.Equals(toolName, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
