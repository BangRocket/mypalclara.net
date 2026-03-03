namespace Clara.Core.Config;

public class SubAgentOptions
{
    public int MaxPerParent { get; set; } = 5;
    public string DefaultTier { get; set; } = "low";
    public int TimeoutMinutes { get; set; } = 10;
}
