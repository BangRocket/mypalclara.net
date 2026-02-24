namespace MyPalClara.Data.Entities;

public class McpRateLimit
{
    public string Id { get; set; } = null!;
    public string? UserId { get; set; }
    public string? ServerName { get; set; }
    public string? ToolName { get; set; }
    public int? MaxCallsPerMinute { get; set; }
    public int? MaxCallsPerHour { get; set; }
    public int? MaxCallsPerDay { get; set; }
    public int CurrentMinuteCount { get; set; }
    public int CurrentHourCount { get; set; }
    public int CurrentDayCount { get; set; }
    public DateTime? MinuteWindowStart { get; set; }
    public DateTime? HourWindowStart { get; set; }
    public DateTime? DayWindowStart { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
