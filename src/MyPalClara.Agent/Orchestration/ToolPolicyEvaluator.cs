using MyPalClara.Core.Configuration;

namespace MyPalClara.Agent.Orchestration;

public enum ToolDecision { Allowed, Blocked, RequiresApproval }

/// <summary>
/// Evaluates tool call requests against configured security policies.
/// Priority: BlockList > AllowList > ApprovalRequired > DefaultMode.
/// </summary>
public sealed class ToolPolicyEvaluator
{
    private readonly ToolSecuritySettings _settings;

    public ToolPolicyEvaluator(ClaraConfig config)
    {
        _settings = config.ToolSecurity;
    }

    public ToolDecision Evaluate(string toolName)
    {
        if (MatchesAny(toolName, _settings.BlockList))
            return ToolDecision.Blocked;

        if (_settings.AllowList.Count > 0 && MatchesAny(toolName, _settings.AllowList))
            return ToolDecision.Allowed;

        if (MatchesAny(toolName, _settings.ApprovalRequired))
            return ToolDecision.RequiresApproval;

        return _settings.DefaultMode switch
        {
            ToolApprovalMode.Block => ToolDecision.Blocked,
            ToolApprovalMode.Approve => ToolDecision.RequiresApproval,
            _ => ToolDecision.Allowed,
        };
    }

    private static bool MatchesAny(string toolName, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.EndsWith('*'))
            {
                if (toolName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (toolName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
