using System.Net.WebSockets;

namespace MyPalClara.Core.Processing;

public class ProcessingContext
{
    public required string RequestId { get; init; }
    public required string ResponseId { get; init; }
    public required string UserId { get; init; }
    public required string ChannelId { get; init; }
    public required string ChannelType { get; init; }
    public required string Content { get; init; }
    public required string Platform { get; init; }
    public required WebSocket WebSocket { get; init; }

    // Optional
    public string? DisplayName { get; init; }
    public string? GuildId { get; init; }
    public string? TierOverride { get; init; }
    public List<Dictionary<string, object>>? ReplyChain { get; init; }
    public List<AttachmentData>? Attachments { get; init; }

    // Computed
    public bool IsDm => ChannelType == "dm";

    // Filled during processing
    public string? DbSessionId { get; set; }
    public string? ModelTier { get; set; }
}

public record AttachmentData(
    string Type,
    string Filename,
    string? MediaType,
    string? Base64Data,
    string? TextContent);
