using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyPalClara.Core.Protocol;

// ========================
// Adapter → Gateway
// ========================

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AuthMessage), "auth")]
[JsonDerivedType(typeof(ChatRequest), "chat")]
[JsonDerivedType(typeof(CommandRequest), "command")]
[JsonDerivedType(typeof(ToolApprovalResponse), "tool_approval_response")]
public abstract record AdapterMessage;

public sealed record AuthMessage(
    string Secret,
    string AdapterType,
    string AdapterId) : AdapterMessage;

public sealed record ChatRequest(
    string ChannelId,
    string ChannelName,
    string ChannelType,
    string UserId,
    string DisplayName,
    string Content,
    string? Tier = null,
    List<string>? Attachments = null) : AdapterMessage;

public sealed record CommandRequest(
    string Command,
    Dictionary<string, JsonElement>? Args = null,
    string? UserId = null) : AdapterMessage;

public sealed record ToolApprovalResponse(
    string RequestId,
    bool Approved) : AdapterMessage;

// ========================
// Gateway → Adapter
// ========================

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AuthResult), "auth_result")]
[JsonDerivedType(typeof(TextChunk), "text_chunk")]
[JsonDerivedType(typeof(ToolStart), "tool_start")]
[JsonDerivedType(typeof(ToolResult), "tool_result")]
[JsonDerivedType(typeof(Complete), "complete")]
[JsonDerivedType(typeof(CommandResult), "command_result")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
[JsonDerivedType(typeof(ToolApprovalRequest), "tool_approval_request")]
public abstract record GatewayResponse;

public sealed record AuthResult(bool Success) : GatewayResponse;

public sealed record TextChunk(string Text) : GatewayResponse;

public sealed record ToolStart(string ToolName, int Step) : GatewayResponse;

public sealed record ToolResult(string ToolName, bool Success, string Preview) : GatewayResponse;

public sealed record Complete(string FullText, int ToolCount) : GatewayResponse;

public sealed record CommandResult(
    string Command,
    bool Success,
    JsonElement? Data = null,
    string? Error = null) : GatewayResponse;

public sealed record ErrorMessage(string Message) : GatewayResponse;

public sealed record ToolApprovalRequest(
    string ToolName,
    string Arguments,
    string RequestId) : GatewayResponse;
