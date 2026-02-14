namespace MyPalClara.Core.Configuration;

public sealed class SlackSettings
{
    public string? AppToken { get; set; }
    public string? BotToken { get; set; }
    public List<string> AllowedChannels { get; set; } = [];
    public int MaxMessageLength { get; set; } = 4000;
}
