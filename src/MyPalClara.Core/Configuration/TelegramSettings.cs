namespace MyPalClara.Core.Configuration;

public sealed class TelegramSettings
{
    public string? Token { get; set; }
    public List<long> AllowedChatIds { get; set; } = [];
    public int MaxMessageLength { get; set; } = 4096;
}
