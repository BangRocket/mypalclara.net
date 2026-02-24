namespace MyPalClara.Data.Entities;

public class McpOAuthToken
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ServerName { get; set; } = null!;
    public string? ServerUrl { get; set; }
    public string? AuthorizationEndpoint { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? RegistrationEndpoint { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }
    public string? CodeVerifier { get; set; }
    public string? StateToken { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public DateTime? ExpiresAt { get; set; }
    public string? Scopes { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime? LastRefreshAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<McpServer> Servers { get; set; } = [];
}
