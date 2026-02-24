namespace MyPalClara.Data.Entities;

public class GoogleOAuthToken
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string AccessToken { get; set; } = null!;
    public string? RefreshToken { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public DateTime? ExpiresAt { get; set; }
    public string? Scopes { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
