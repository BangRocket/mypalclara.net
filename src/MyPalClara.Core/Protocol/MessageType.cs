namespace MyPalClara.Core.Protocol;

/// <summary>
/// All message types in the gateway WebSocket protocol.
/// Values match the Python gateway protocol exactly.
/// </summary>
public static class MessageType
{
    // Registration
    public const string Register = "register";
    public const string Registered = "registered";
    public const string Unregister = "unregister";

    // Heartbeat
    public const string Ping = "ping";
    public const string Pong = "pong";

    // Message flow
    public const string Message = "message";
    public const string ResponseStart = "response_start";
    public const string ResponseChunk = "response_chunk";
    public const string ResponseEnd = "response_end";

    // Tool execution
    public const string ToolStart = "tool_start";
    public const string ToolResult = "tool_result";

    // Control
    public const string Cancel = "cancel";
    public const string Cancelled = "cancelled";
    public const string Error = "error";
    public const string Status = "status";

    // Proactive (ORS)
    public const string ProactiveMessage = "proactive_message";

    // MCP Management
    public const string McpList = "mcp_list";
    public const string McpListResponse = "mcp_list_response";
    public const string McpInstall = "mcp_install";
    public const string McpInstallResponse = "mcp_install_response";
    public const string McpUninstall = "mcp_uninstall";
    public const string McpUninstallResponse = "mcp_uninstall_response";
    public const string McpStatus = "mcp_status";
    public const string McpStatusResponse = "mcp_status_response";
    public const string McpRestart = "mcp_restart";
    public const string McpRestartResponse = "mcp_restart_response";
    public const string McpEnable = "mcp_enable";
    public const string McpEnableResponse = "mcp_enable_response";
}
