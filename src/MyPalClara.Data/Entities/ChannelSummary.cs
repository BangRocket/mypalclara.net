namespace MyPalClara.Data.Entities;

public class ChannelSummary
{
    public string Id { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public string Summary { get; set; } = "";
    public DateTime? SummaryCutoffAt { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
}
