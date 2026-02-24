namespace MyPalClara.Data.Entities;

public class ToolAuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string UserId { get; set; } = null!;
    public string ToolName { get; set; } = null!;
    public string Platform { get; set; } = null!;
    public string? Parameters { get; set; }
    public string ResultStatus { get; set; } = null!;
    public string? ErrorMessage { get; set; }
    public int? ExecutionTimeMs { get; set; }
    public string? RiskLevel { get; set; }
    public string? Intent { get; set; }
    public string? ChannelId { get; set; }
}
