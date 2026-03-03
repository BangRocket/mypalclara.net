namespace Clara.Core.Data.Entities;

public class ToolUsageEntity
{
    public Guid Id { get; set; }
    public string ToolName { get; set; } = "";
    public string SessionKey { get; set; } = "";
    public string? UserId { get; set; }
    public string? Arguments { get; set; }
    public bool Success { get; set; }
    public int DurationMs { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
}
