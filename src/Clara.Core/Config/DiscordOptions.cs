namespace Clara.Core.Config;

public class DiscordOptions
{
    public string? BotToken { get; set; }
    public List<string> AllowedServers { get; set; } = [];
    public int MaxImages { get; set; } = 1;
    public int MaxImageDimension { get; set; } = 1568;
    public List<string> StopPhrases { get; set; } = ["clara stop", "stop clara", "nevermind", "never mind"];
}
