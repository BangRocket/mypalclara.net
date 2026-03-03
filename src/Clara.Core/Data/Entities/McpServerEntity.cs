namespace Clara.Core.Data.Entities;

public class McpServerEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Transport { get; set; } = "stdio";
    public string Command { get; set; } = "";
    public string? Args { get; set; }
    public string? Env { get; set; }
    public string? OAuthConfig { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
