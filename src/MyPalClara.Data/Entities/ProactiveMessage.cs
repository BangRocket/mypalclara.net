namespace MyPalClara.Data.Entities;

public class ProactiveMessage
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string Priority { get; set; } = null!;
    public string? Reason { get; set; }
    public DateTime SentAt { get; set; }
    public string ResponseReceived { get; set; } = "false";
    public DateTime? ResponseAt { get; set; }
}
