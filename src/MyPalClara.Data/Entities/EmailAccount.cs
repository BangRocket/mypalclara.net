namespace MyPalClara.Data.Entities;

public class EmailAccount
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string EmailAddress { get; set; } = null!;
    public string ProviderType { get; set; } = null!;
    public string? ImapServer { get; set; }
    public int? ImapPort { get; set; }
    public string? ImapUsername { get; set; }
    public string? ImapPassword { get; set; }
    public string Enabled { get; set; } = "true";
    public int PollIntervalMinutes { get; set; } = 5;
    public DateTime? LastCheckedAt { get; set; }
    public string? LastSeenUid { get; set; }
    public DateTime? LastSeenTimestamp { get; set; }
    public string Status { get; set; } = "active";
    public string? LastError { get; set; }
    public int ErrorCount { get; set; }
    public string? AlertChannelId { get; set; }
    public string PingOnAlert { get; set; } = "false";
    public int? QuietHoursStart { get; set; }
    public int? QuietHoursEnd { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<EmailRule> EmailRules { get; set; } = [];
    public ICollection<EmailAlert> EmailAlerts { get; set; } = [];
}
