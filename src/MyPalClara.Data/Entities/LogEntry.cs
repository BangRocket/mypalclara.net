namespace MyPalClara.Data.Entities;

public class LogEntry
{
    public string Id { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = null!;
    public string LoggerName { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? Module { get; set; }
    public string? Function { get; set; }
    public int? LineNumber { get; set; }
    public string? Exception { get; set; }
    public string? ExtraData { get; set; }
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
}
