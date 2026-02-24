namespace MyPalClara.Data.Entities;

public class EmailAlert
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string AccountId { get; set; } = null!;
    public string? RuleId { get; set; }
    public string EmailUid { get; set; } = null!;
    public string EmailFrom { get; set; } = null!;
    public string EmailSubject { get; set; } = null!;
    public string? EmailSnippet { get; set; }
    public DateTime? EmailReceivedAt { get; set; }
    public string ChannelId { get; set; } = null!;
    public string? MessageId { get; set; }
    public string Importance { get; set; } = null!;
    public string WasPinged { get; set; } = "false";
    public DateTime? SentAt { get; set; }

    public EmailAccount Account { get; set; } = null!;
    public EmailRule? Rule { get; set; }
}
