namespace MyPalClara.Data.Entities;

public class McpServer
{
    public string Id { get; set; } = null!;
    public string? UserId { get; set; }
    public string Name { get; set; } = null!;
    public string ServerType { get; set; } = null!;
    public string? SourceType { get; set; }
    public string? SourceUrl { get; set; }
    public string? ConfigPath { get; set; }
    public bool Enabled { get; set; } = true;
    public string Status { get; set; } = "stopped";
    public int ToolCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public bool OAuthRequired { get; set; }
    public string? OAuthTokenId { get; set; }
    public int TotalToolCalls { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? InstalledBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public McpOAuthToken? OAuthToken { get; set; }
    public ICollection<McpToolCall> ToolCalls { get; set; } = [];
}
