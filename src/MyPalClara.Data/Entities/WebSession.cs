namespace MyPalClara.Data.Entities;

public class WebSession
{
    public string Id { get; set; } = null!;
    public string CanonicalUserId { get; set; } = null!;
    public string SessionTokenHash { get; set; } = null!;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool Revoked { get; set; }

    public CanonicalUser CanonicalUser { get; set; } = null!;
}
