namespace MyPalClara.Core.Configuration;

public sealed class AgentSettings
{
    public bool MultiAgentEnabled { get; set; } = false;
    public Dictionary<string, AgentProfile> Profiles { get; set; } = [];
    public Dictionary<string, string> ChannelRouting { get; set; } = [];
}

public sealed class AgentProfile
{
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string ModelHigh { get; set; } = "";
    public string ModelMid { get; set; } = "";
    public string ModelLow { get; set; } = "";
    public string PersonalityFile { get; set; } = "";
    public List<string> McpServers { get; set; } = [];
    public int MaxToolIterations { get; set; } = 75;
}
