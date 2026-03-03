namespace Clara.Core.Config;

public class ClaraOptions
{
    public LlmOptions Llm { get; set; } = new();
    public MemoryOptions Memory { get; set; } = new();
    public GatewayOptions Gateway { get; set; } = new();
    public ToolOptions Tools { get; set; } = new();
    public SandboxOptions Sandbox { get; set; } = new();
    public HeartbeatOptions Heartbeat { get; set; } = new();
    public SubAgentOptions SubAgents { get; set; } = new();
    public DiscordOptions Discord { get; set; } = new();
}
