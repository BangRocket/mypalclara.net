namespace MyPalClara.Core.Configuration;

/// <summary>Root configuration POCO bound from appsettings.json.</summary>
public sealed class ClaraConfig
{
    public string UserId { get; set; } = "demo-user";
    public string? LinkTo { get; set; }
    public string DefaultProject { get; set; } = "Default Project";
    public string DefaultTimezone { get; set; } = "America/New_York";
    public string DataDir { get; set; } = ".";
    public string FilesDir { get; set; } = "./clara_files";
    public int MaxFileSize { get; set; } = 52428800;

    public LlmSettings Llm { get; set; } = new();
    public MemorySettings Memory { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
    public GatewaySettings Gateway { get; set; } = new();
    public BotSettings Bot { get; set; } = new();
    public McpSettings Mcp { get; set; } = new();
    public VoiceSettings Voice { get; set; } = new();
    public DiscordSettings Discord { get; set; } = new();
    public SshSettings Ssh { get; set; } = new();
    public SkillsSettings Skills { get; set; } = new();
    public TelegramSettings Telegram { get; set; } = new();
    public SlackSettings Slack { get; set; } = new();
    public WhatsAppSettings WhatsApp { get; set; } = new();
    public BrowserSettings Browser { get; set; } = new();
    public ToolSecuritySettings ToolSecurity { get; set; } = new();
    public SchedulerSettings Scheduler { get; set; } = new();
    public AgentSettings Agents { get; set; } = new();
    public SignalSettings Signal { get; set; } = new();
}
