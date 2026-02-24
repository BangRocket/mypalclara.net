using System.Net.WebSockets;
using System.Text.Json;

namespace MyPalClara.Core.Router;

public class QueuedRequest
{
    public required string RequestId { get; init; }
    public required string ChannelId { get; init; }
    public required string UserId { get; init; }
    public required string Content { get; init; }
    public required WebSocket WebSocket { get; init; }
    public required string NodeId { get; init; }
    public DateTime QueuedAt { get; init; } = DateTime.UtcNow;
    public int Position { get; set; }
    public bool IsBatchable { get; set; }

    /// <summary>
    /// Store the full deserialized request data as JsonElement for later processing.
    /// </summary>
    public JsonElement RawRequest { get; init; }
}
