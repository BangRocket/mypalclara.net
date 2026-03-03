namespace Clara.Core.Tools.ToolPolicy;

public interface IToolPolicy
{
    int Priority { get; }
    ToolPolicyDecision Evaluate(string toolName, ToolExecutionContext context);
}
