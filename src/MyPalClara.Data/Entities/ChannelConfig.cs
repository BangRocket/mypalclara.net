namespace MyPalClara.Data.Entities;

public class ChannelConfig
{
    public string Id { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public string GuildId { get; set; } = null!;
    public string Mode { get; set; } = "mention";
    public string? ConfiguredBy { get; set; }
    public DateTime? ConfiguredAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
