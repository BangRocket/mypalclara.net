namespace MyPalClara.Data.Entities;

public class OAuthToken
{
    public string Id { get; set; } = null!;
    public string CanonicalUserId { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string AccessToken { get; set; } = null!;
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Scopes { get; set; }
    public string? ProviderUserId { get; set; }
    public string? ProviderData { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public CanonicalUser CanonicalUser { get; set; } = null!;
}
