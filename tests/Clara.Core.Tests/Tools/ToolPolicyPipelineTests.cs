using Clara.Core.Config;
using Clara.Core.Tools;
using Clara.Core.Tools.ToolPolicy;

namespace Clara.Core.Tests.Tools;

public class ToolPolicyPipelineTests
{
    private static ToolExecutionContext MakeContext(
        bool sandboxed = false, string platform = "discord", string sessionKey = "general") =>
        new("user1", sessionKey, platform, sandboxed, null);

    [Fact]
    public void Default_allow_when_no_policies()
    {
        var pipeline = new ToolPolicyPipeline([]);

        Assert.True(pipeline.IsAllowed("anything", MakeContext()));
    }

    [Fact]
    public void Default_allow_when_all_abstain()
    {
        var policies = new IToolPolicy[] { new AbstainPolicy(1) };
        var pipeline = new ToolPolicyPipeline(policies);

        Assert.True(pipeline.IsAllowed("anything", MakeContext()));
    }

    [Fact]
    public void First_deny_wins()
    {
        var policies = new IToolPolicy[]
        {
            new FixedPolicy(1, ToolPolicyDecision.Deny),
            new FixedPolicy(2, ToolPolicyDecision.Allow),
        };
        var pipeline = new ToolPolicyPipeline(policies);

        Assert.False(pipeline.IsAllowed("anything", MakeContext()));
    }

    [Fact]
    public void First_allow_wins()
    {
        var policies = new IToolPolicy[]
        {
            new FixedPolicy(1, ToolPolicyDecision.Allow),
            new FixedPolicy(2, ToolPolicyDecision.Deny),
        };
        var pipeline = new ToolPolicyPipeline(policies);

        Assert.True(pipeline.IsAllowed("anything", MakeContext()));
    }

    [Fact]
    public void Priority_ordering_is_respected()
    {
        // Higher priority (lower number) deny should beat later allow
        var policies = new IToolPolicy[]
        {
            new FixedPolicy(200, ToolPolicyDecision.Allow),
            new FixedPolicy(100, ToolPolicyDecision.Deny),
        };
        var pipeline = new ToolPolicyPipeline(policies);

        // Priority 100 evaluates first → Deny
        Assert.False(pipeline.IsAllowed("anything", MakeContext()));
    }

    [Fact]
    public void Sandbox_policy_denies_dangerous_tools()
    {
        var policies = new IToolPolicy[] { new SandboxToolPolicy() };
        var pipeline = new ToolPolicyPipeline(policies);

        Assert.False(pipeline.IsAllowed("shell_execute", MakeContext(sandboxed: true)));
        Assert.False(pipeline.IsAllowed("file_write", MakeContext(sandboxed: true)));
        Assert.False(pipeline.IsAllowed("file_delete", MakeContext(sandboxed: true)));
    }

    [Fact]
    public void Sandbox_policy_allows_safe_tools_when_sandboxed()
    {
        var policies = new IToolPolicy[] { new SandboxToolPolicy() };
        var pipeline = new ToolPolicyPipeline(policies);

        // Safe tools abstain → default allow
        Assert.True(pipeline.IsAllowed("file_read", MakeContext(sandboxed: true)));
        Assert.True(pipeline.IsAllowed("web_search", MakeContext(sandboxed: true)));
    }

    [Fact]
    public void Sandbox_policy_abstains_when_not_sandboxed()
    {
        var policies = new IToolPolicy[] { new SandboxToolPolicy() };
        var pipeline = new ToolPolicyPipeline(policies);

        // Not sandboxed → abstain → default allow
        Assert.True(pipeline.IsAllowed("shell_execute", MakeContext(sandboxed: false)));
    }

    [Fact]
    public void Agent_policy_deny_list_blocks_tool()
    {
        var options = new ToolPolicyOptions { Allow = ["*"], Deny = ["shell_execute"] };
        var policies = new IToolPolicy[] { new AgentToolPolicy(options) };
        var pipeline = new ToolPolicyPipeline(policies);

        Assert.False(pipeline.IsAllowed("shell_execute", MakeContext()));
        Assert.True(pipeline.IsAllowed("file_read", MakeContext()));
    }

    [Fact]
    public void Agent_policy_wildcard_allow()
    {
        var options = new ToolPolicyOptions { Allow = ["file_*"], Deny = [] };
        var policies = new IToolPolicy[] { new AgentToolPolicy(options) };
        var pipeline = new ToolPolicyPipeline(policies);

        Assert.True(pipeline.IsAllowed("file_read", MakeContext()));
        Assert.True(pipeline.IsAllowed("file_write", MakeContext()));
    }

    [Fact]
    public void Channel_policy_restricts_specific_channel()
    {
        var channelPolicies = new Dictionary<string, ToolPolicyOptions>
        {
            ["discord:*"] = new ToolPolicyOptions { Allow = [], Deny = ["shell_execute"] }
        };
        var policies = new IToolPolicy[] { new ChannelToolPolicy(channelPolicies) };
        var pipeline = new ToolPolicyPipeline(policies);

        Assert.False(pipeline.IsAllowed("shell_execute", MakeContext(platform: "discord", sessionKey: "general")));
        // Different platform → no match → abstain → default allow
        Assert.True(pipeline.IsAllowed("shell_execute", MakeContext(platform: "slack", sessionKey: "general")));
    }

    // --- Test helpers ---

    private class FixedPolicy : IToolPolicy
    {
        private readonly ToolPolicyDecision _decision;
        public FixedPolicy(int priority, ToolPolicyDecision decision)
        {
            Priority = priority;
            _decision = decision;
        }
        public int Priority { get; }
        public ToolPolicyDecision Evaluate(string toolName, ToolExecutionContext context) => _decision;
    }

    private class AbstainPolicy : IToolPolicy
    {
        public AbstainPolicy(int priority) => Priority = priority;
        public int Priority { get; }
        public ToolPolicyDecision Evaluate(string toolName, ToolExecutionContext context) => ToolPolicyDecision.Abstain;
    }
}
