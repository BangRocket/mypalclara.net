namespace MyPalClara.Data.Entities;

public class CanonicalUser
{
    public string Id { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? PrimaryEmail { get; set; }
    public string? AvatarUrl { get; set; }
    public string Status { get; set; } = "active";
    public bool IsAdmin { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<PlatformLink> PlatformLinks { get; set; } = [];
    public ICollection<OAuthToken> OAuthTokens { get; set; } = [];
    public ICollection<WebSession> WebSessions { get; set; } = [];
}
