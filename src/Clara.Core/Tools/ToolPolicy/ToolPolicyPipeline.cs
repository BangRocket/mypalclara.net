namespace Clara.Core.Tools.ToolPolicy;

public class ToolPolicyPipeline
{
    private readonly IEnumerable<IToolPolicy> _policies;

    public ToolPolicyPipeline(IEnumerable<IToolPolicy> policies) => _policies = policies;

    public bool IsAllowed(string toolName, ToolExecutionContext context)
    {
        foreach (var policy in _policies.OrderBy(p => p.Priority))
        {
            var decision = policy.Evaluate(toolName, context);
            if (decision == ToolPolicyDecision.Deny) return false;
            if (decision == ToolPolicyDecision.Allow) return true;
        }
        return true; // default allow
    }
}
