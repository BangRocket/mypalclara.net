namespace MyPalClara.Data.Entities;

public class McpToolCall
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? SessionId { get; set; }
    public string? RequestId { get; set; }
    public string? ServerId { get; set; }
    public string ServerName { get; set; } = null!;
    public string ToolName { get; set; } = null!;
    public string? Arguments { get; set; }
    public string? ResultPreview { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? ErrorType { get; set; }

    public McpServer? Server { get; set; }
}
