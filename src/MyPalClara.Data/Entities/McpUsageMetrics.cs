namespace MyPalClara.Data.Entities;

public class McpUsageMetrics
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ServerName { get; set; } = null!;
    public string Date { get; set; } = null!;
    public int CallCount { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public int TimeoutCount { get; set; }
    public int TotalDurationMs { get; set; }
    public double AvgDurationMs { get; set; }
    public string? ToolCounts { get; set; }
    public DateTime? FirstCallAt { get; set; }
    public DateTime? LastCallAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
