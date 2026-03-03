namespace Clara.Core.Data.Entities;

public class EmailAccountEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 300;
    public DateTime? LastPolledAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
