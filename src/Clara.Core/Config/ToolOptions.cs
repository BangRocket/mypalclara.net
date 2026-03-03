namespace Clara.Core.Config;

public class ToolOptions
{
    public LoopDetectionOptions LoopDetection { get; set; } = new();
    public string DescriptionTier { get; set; } = "low";
    public int DescriptionMaxWords { get; set; } = 20;
    public ToolPolicyOptions DefaultPolicy { get; set; } = new();
    public Dictionary<string, ToolPolicyOptions> ChannelPolicies { get; set; } = new();
}

public class LoopDetectionOptions
{
    public int MaxIdenticalCalls { get; set; } = 3;
    public int MaxTotalRounds { get; set; } = 10;
}

public class ToolPolicyOptions
{
    public List<string> Allow { get; set; } = ["*"];
    public List<string> Deny { get; set; } = [];
}
