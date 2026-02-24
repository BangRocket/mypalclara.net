namespace MyPalClara.Data.Entities;

public class GuildConfig
{
    public int Id { get; set; }
    public string GuildId { get; set; } = null!;
    public string? DefaultTier { get; set; }
    public string AutoTierEnabled { get; set; } = "false";
    public string OrsEnabled { get; set; } = "false";
    public string? OrsChannelId { get; set; }
    public string? OrsQuietStart { get; set; }
    public string? OrsQuietEnd { get; set; }
    public string SandboxMode { get; set; } = "auto";
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
