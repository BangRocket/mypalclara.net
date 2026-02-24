using System.Text.Json.Serialization;

namespace MyPalClara.Core.Protocol;

// ============================================================================
// Shared Sub-Models
// ============================================================================

/// <summary>Information about a user.</summary>
public record UserInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("platform_id")] string PlatformId,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("display_name")] string? DisplayName = null);

/// <summary>Information about a channel.</summary>
public record ChannelInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type = "server",
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("guild_id")] string? GuildId = null,
    [property: JsonPropertyName("guild_name")] string? GuildName = null);

/// <summary>Information about a message attachment.</summary>
public record AttachmentInfo(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("media_type")] string? MediaType = null,
    [property: JsonPropertyName("base64_data")] string? Base64Data = null,
    [property: JsonPropertyName("content")] string? Content = null,
    [property: JsonPropertyName("size")] int? Size = null);

/// <summary>File data for sending as attachment over WebSocket.</summary>
public record FileData(
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("content_base64")] string ContentBase64,
    [property: JsonPropertyName("media_type")] string MediaType = "application/octet-stream");

/// <summary>Information about an interactive button.</summary>
public record ButtonInfo(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("style")] string Style = "secondary",
    [property: JsonPropertyName("action")] string Action = "dismiss",
    [property: JsonPropertyName("disabled")] bool Disabled = false);

/// <summary>Information about an MCP server.</summary>
public record McpServerInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("enabled")] bool Enabled = true,
    [property: JsonPropertyName("connected")] bool Connected = false,
    [property: JsonPropertyName("tool_count")] int ToolCount = 0,
    [property: JsonPropertyName("source_type")] string SourceType = "unknown",
    [property: JsonPropertyName("transport")] string? Transport = null,
    [property: JsonPropertyName("tools")] List<string>? Tools = null,
    [property: JsonPropertyName("last_error")] string? LastError = null);

// ============================================================================
// Adapter -> Gateway Messages
// ============================================================================

/// <summary>Adapter -> Gateway: Register a new adapter node.</summary>
public record RegisterMessage(
    [property: JsonPropertyName("node_id")] string NodeId,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("capabilities")] List<string>? Capabilities = null,
    [property: JsonPropertyName("metadata")] Dictionary<string, object>? Metadata = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.Register;
}

/// <summary>Adapter -> Gateway: Process a user message.</summary>
public record MessageRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("user")] UserInfo User,
    [property: JsonPropertyName("channel")] ChannelInfo Channel,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("attachments")] List<AttachmentInfo>? Attachments = null,
    [property: JsonPropertyName("reply_chain")] List<Dictionary<string, object>>? ReplyChain = null,
    [property: JsonPropertyName("tier_override")] string? TierOverride = null,
    [property: JsonPropertyName("metadata")] Dictionary<string, object>? Metadata = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.Message;
}

/// <summary>Adapter -> Gateway: Cancel in-flight request.</summary>
public record CancelMessage(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("reason")] string? Reason = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.Cancel;
}

/// <summary>Bidirectional heartbeat ping.</summary>
public record PingMessage
{
    [JsonPropertyName("type")]
    public string Type => MessageType.Ping;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = DateTime.UtcNow.ToString("O");
}

/// <summary>Bidirectional status request.</summary>
public record StatusRequestMessage(
    [property: JsonPropertyName("node_id")] string? NodeId = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.Status;
}

// ============================================================================
// Gateway -> Adapter Messages
// ============================================================================

/// <summary>Gateway -> Adapter: Confirm registration.</summary>
public record RegisteredMessage(
    [property: JsonPropertyName("node_id")] string NodeId,
    [property: JsonPropertyName("session_id")] string SessionId)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.Registered;

    [JsonPropertyName("server_time")]
    public string ServerTime { get; init; } = DateTime.UtcNow.ToString("O");
}

/// <summary>Bidirectional heartbeat pong.</summary>
public record PongMessage
{
    [JsonPropertyName("type")]
    public string Type => MessageType.Pong;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = DateTime.UtcNow.ToString("O");
}

/// <summary>Gateway -> Adapter: Response generation started.</summary>
public record ResponseStartMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("model_tier")] string? ModelTier = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.ResponseStart;
}

/// <summary>Gateway -> Adapter: Streaming response chunk.</summary>
public record ResponseChunkMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("chunk")] string Chunk,
    [property: JsonPropertyName("accumulated")] string? Accumulated = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.ResponseChunk;
}

/// <summary>Gateway -> Adapter: Response generation complete.</summary>
public record ResponseEndMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("full_text")] string FullText,
    [property: JsonPropertyName("files")] List<string>? Files = null,
    [property: JsonPropertyName("file_data")] List<FileData>? FileDataList = null,
    [property: JsonPropertyName("tool_count")] int ToolCount = 0,
    [property: JsonPropertyName("tokens_used")] int? TokensUsed = null,
    [property: JsonPropertyName("edit_target")] string? EditTarget = null,
    [property: JsonPropertyName("components")] List<ButtonInfo>? Components = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.ResponseEnd;
}

/// <summary>Gateway -> Adapter: Tool execution started.</summary>
public record ToolStartMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("tool_name")] string ToolName,
    [property: JsonPropertyName("step")] int Step,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("emoji")] string Emoji = "\u2699\uFE0F")
{
    [JsonPropertyName("type")]
    public string Type => MessageType.ToolStart;
}

/// <summary>Gateway -> Adapter: Tool execution completed.</summary>
public record ToolResultMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("tool_name")] string ToolName,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("output_preview")] string? OutputPreview = null,
    [property: JsonPropertyName("duration_ms")] int? DurationMs = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.ToolResult;
}

/// <summary>Gateway -> Adapter: Request was cancelled.</summary>
public record CancelledMessage(
    [property: JsonPropertyName("request_id")] string RequestId)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.Cancelled;
}

/// <summary>Gateway -> Adapter: Error occurred.</summary>
public record ErrorMessage(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string ErrorDetail,
    [property: JsonPropertyName("request_id")] string? RequestId = null,
    [property: JsonPropertyName("recoverable")] bool Recoverable = true)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.Error;
}

/// <summary>Bidirectional status information.</summary>
public record StatusMessage(
    [property: JsonPropertyName("node_id")] string? NodeId = null,
    [property: JsonPropertyName("active_requests")] int ActiveRequests = 0,
    [property: JsonPropertyName("queue_length")] int QueueLength = 0,
    [property: JsonPropertyName("uptime_seconds")] int? UptimeSeconds = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.Status;
}

/// <summary>Gateway -> Adapter: Proactive message from ORS.</summary>
public record ProactiveMessage(
    [property: JsonPropertyName("user")] UserInfo User,
    [property: JsonPropertyName("channel")] ChannelInfo Channel,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("priority")] string Priority = "normal")
{
    [JsonPropertyName("type")]
    public string Type => MessageType.ProactiveMessage;
}

// ============================================================================
// MCP Management Messages
// ============================================================================

/// <summary>Adapter -> Gateway: List all MCP servers.</summary>
public record McpListRequest(
    [property: JsonPropertyName("request_id")] string RequestId)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpList;
}

/// <summary>Gateway -> Adapter: List of MCP servers.</summary>
public record McpListResponse(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("success")] bool Success = true,
    [property: JsonPropertyName("servers")] List<McpServerInfo>? Servers = null,
    [property: JsonPropertyName("error")] string? Error = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpListResponse;
}

/// <summary>Adapter -> Gateway: Install an MCP server.</summary>
public record McpInstallRequest(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("requested_by")] string? RequestedBy = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpInstall;
}

/// <summary>Gateway -> Adapter: Installation result.</summary>
public record McpInstallResponse(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("server_name")] string? ServerName = null,
    [property: JsonPropertyName("tools_discovered")] int ToolsDiscovered = 0,
    [property: JsonPropertyName("error")] string? Error = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpInstallResponse;
}

/// <summary>Adapter -> Gateway: Uninstall an MCP server.</summary>
public record McpUninstallRequest(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("server_name")] string ServerName)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpUninstall;
}

/// <summary>Gateway -> Adapter: Uninstall result.</summary>
public record McpUninstallResponse(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string? Error = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpUninstallResponse;
}

/// <summary>Adapter -> Gateway: Get status of an MCP server.</summary>
public record McpStatusRequest(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("server_name")] string? ServerName = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpStatus;
}

/// <summary>Gateway -> Adapter: Server status.</summary>
public record McpStatusResponse(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("success")] bool Success = true,
    [property: JsonPropertyName("server")] McpServerInfo? Server = null,
    [property: JsonPropertyName("total_servers")] int TotalServers = 0,
    [property: JsonPropertyName("connected_servers")] int ConnectedServers = 0,
    [property: JsonPropertyName("enabled_servers")] int EnabledServers = 0,
    [property: JsonPropertyName("error")] string? Error = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpStatusResponse;
}

/// <summary>Adapter -> Gateway: Restart an MCP server.</summary>
public record McpRestartRequest(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("server_name")] string ServerName)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpRestart;
}

/// <summary>Gateway -> Adapter: Restart result.</summary>
public record McpRestartResponse(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string? Error = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpRestartResponse;
}

/// <summary>Adapter -> Gateway: Enable or disable an MCP server.</summary>
public record McpEnableRequest(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("server_name")] string ServerName,
    [property: JsonPropertyName("enabled")] bool Enabled)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpEnable;
}

/// <summary>Gateway -> Adapter: Enable/disable result.</summary>
public record McpEnableResponse(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("error")] string? Error = null)
{
    [JsonPropertyName("type")]
    public string Type => MessageType.McpEnableResponse;
}
