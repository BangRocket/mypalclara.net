using MyPalClara.Core.Configuration;
using MyPalClara.Agent.Orchestration;

namespace MyPalClara.Agent.Tests;

public class ToolPolicyEvaluatorTests
{
    private static ToolPolicyEvaluator CreateEvaluator(Action<ToolSecuritySettings> configure)
    {
        var settings = new ToolSecuritySettings();
        configure(settings);
        var config = new ClaraConfig { ToolSecurity = settings };
        return new ToolPolicyEvaluator(config);
    }

    [Fact]
    public void DefaultMode_Allow_AllowsAllTools()
    {
        var evaluator = CreateEvaluator(s => s.DefaultMode = ToolApprovalMode.Allow);

        Assert.Equal(ToolDecision.Allowed, evaluator.Evaluate("any_tool"));
    }

    [Fact]
    public void DefaultMode_Block_BlocksAllTools()
    {
        var evaluator = CreateEvaluator(s => s.DefaultMode = ToolApprovalMode.Block);

        Assert.Equal(ToolDecision.Blocked, evaluator.Evaluate("any_tool"));
    }

    [Fact]
    public void DefaultMode_Approve_RequiresApproval()
    {
        var evaluator = CreateEvaluator(s => s.DefaultMode = ToolApprovalMode.Approve);

        Assert.Equal(ToolDecision.RequiresApproval, evaluator.Evaluate("any_tool"));
    }

    [Fact]
    public void BlockList_ExactMatch_Blocks()
    {
        var evaluator = CreateEvaluator(s => s.BlockList = ["dangerous_tool"]);

        Assert.Equal(ToolDecision.Blocked, evaluator.Evaluate("dangerous_tool"));
        Assert.Equal(ToolDecision.Allowed, evaluator.Evaluate("safe_tool"));
    }

    [Fact]
    public void BlockList_GlobPrefix_Blocks()
    {
        var evaluator = CreateEvaluator(s => s.BlockList = ["shell__*"]);

        Assert.Equal(ToolDecision.Blocked, evaluator.Evaluate("shell__execute"));
        Assert.Equal(ToolDecision.Blocked, evaluator.Evaluate("shell__spawn"));
        Assert.Equal(ToolDecision.Allowed, evaluator.Evaluate("memory__search"));
    }

    [Fact]
    public void AllowList_ExactMatch_Allows()
    {
        var evaluator = CreateEvaluator(s =>
        {
            s.DefaultMode = ToolApprovalMode.Block;
            s.AllowList = ["memory__search"];
        });

        Assert.Equal(ToolDecision.Allowed, evaluator.Evaluate("memory__search"));
        Assert.Equal(ToolDecision.Blocked, evaluator.Evaluate("other_tool"));
    }

    [Fact]
    public void AllowList_GlobPrefix_Allows()
    {
        var evaluator = CreateEvaluator(s =>
        {
            s.DefaultMode = ToolApprovalMode.Block;
            s.AllowList = ["memory__*"];
        });

        Assert.Equal(ToolDecision.Allowed, evaluator.Evaluate("memory__search"));
        Assert.Equal(ToolDecision.Allowed, evaluator.Evaluate("memory__add"));
        Assert.Equal(ToolDecision.Blocked, evaluator.Evaluate("shell__exec"));
    }

    [Fact]
    public void ApprovalRequired_Overrides_DefaultAllow()
    {
        var evaluator = CreateEvaluator(s =>
        {
            s.DefaultMode = ToolApprovalMode.Allow;
            s.ApprovalRequired = ["file__delete"];
        });

        Assert.Equal(ToolDecision.RequiresApproval, evaluator.Evaluate("file__delete"));
        Assert.Equal(ToolDecision.Allowed, evaluator.Evaluate("file__read"));
    }

    [Fact]
    public void BlockList_TakesPrecedence_OverAllowList()
    {
        var evaluator = CreateEvaluator(s =>
        {
            s.BlockList = ["dangerous__*"];
            s.AllowList = ["dangerous__safe"];
        });

        // BlockList wins even when AllowList also matches
        Assert.Equal(ToolDecision.Blocked, evaluator.Evaluate("dangerous__safe"));
    }

    [Fact]
    public void AllowList_TakesPrecedence_OverApprovalRequired()
    {
        var evaluator = CreateEvaluator(s =>
        {
            s.AllowList = ["tool__*"];
            s.ApprovalRequired = ["tool__sensitive"];
        });

        // AllowList wins over ApprovalRequired
        Assert.Equal(ToolDecision.Allowed, evaluator.Evaluate("tool__sensitive"));
    }

    [Fact]
    public void CaseInsensitive_Matching()
    {
        var evaluator = CreateEvaluator(s => s.BlockList = ["Shell__Execute"]);

        Assert.Equal(ToolDecision.Blocked, evaluator.Evaluate("shell__execute"));
        Assert.Equal(ToolDecision.Blocked, evaluator.Evaluate("SHELL__EXECUTE"));
    }
}
