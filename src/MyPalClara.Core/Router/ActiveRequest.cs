using System.Net.WebSockets;

namespace MyPalClara.Core.Router;

public class ActiveRequest
{
    public required QueuedRequest Request { get; init; }
    public CancellationTokenSource Cts { get; init; } = new();
    public Task? ProcessingTask { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public int ToolCount { get; set; }
}
