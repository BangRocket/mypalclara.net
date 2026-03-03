namespace Clara.Core.Tools.ToolPolicy;

/// <summary>
/// When context.IsSandboxed is true, denies dangerous tools.
/// Priority 50 (evaluated before agent/channel policies).
/// </summary>
public class SandboxToolPolicy : IToolPolicy
{
    private static readonly HashSet<string> DangerousTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "shell_execute",
        "file_write",
        "file_delete",
    };

    public int Priority => 50;

    public ToolPolicyDecision Evaluate(string toolName, ToolExecutionContext context)
    {
        if (!context.IsSandboxed)
            return ToolPolicyDecision.Abstain;

        if (DangerousTools.Contains(toolName))
            return ToolPolicyDecision.Deny;

        return ToolPolicyDecision.Abstain;
    }
}
