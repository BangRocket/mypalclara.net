using System.Text;
using System.Text.Json;
using Clara.Core.Sessions;

namespace Clara.Core.Tools.BuiltIn;

public class SessionsListTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {}
        }
        """).RootElement;

    private readonly ISessionManager _sessionManager;

    public SessionsListTool(ISessionManager sessionManager) => _sessionManager = sessionManager;

    public string Name => "sessions_list";
    public string Description => "List active sessions";
    public ToolCategory Category => ToolCategory.Session;
    public JsonElement ParameterSchema => Schema;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        // SessionManager currently only supports get-by-key, not list-all.
        // For now, return info about the current session.
        return Task.FromResult(ToolResult.Ok($"Current session: {context.SessionKey} (platform: {context.Platform})"));
    }
}

public class SessionsHistoryTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "session_key": { "type": "string", "description": "Session key (default: current session)" },
                "limit": { "type": "integer", "description": "Max messages to return (default 20)" }
            }
        }
        """).RootElement;

    private readonly ISessionManager _sessionManager;

    public SessionsHistoryTool(ISessionManager sessionManager) => _sessionManager = sessionManager;

    public string Name => "sessions_history";
    public string Description => "Get message history from a session";
    public ToolCategory Category => ToolCategory.Session;
    public JsonElement ParameterSchema => Schema;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        var sessionKey = context.SessionKey;
        if (arguments.TryGetProperty("session_key", out var keyEl))
            sessionKey = keyEl.GetString() ?? sessionKey;

        var limit = 20;
        if (arguments.TryGetProperty("limit", out var limitEl))
            limit = limitEl.GetInt32();

        var session = await _sessionManager.GetAsync(sessionKey, ct);
        if (session is null)
            return ToolResult.Fail($"Session not found: {sessionKey}");

        var messages = session.Messages.TakeLast(limit);
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            sb.AppendLine($"[{msg.Role}]");
            foreach (var content in msg.Content)
            {
                if (content is Llm.TextContent text)
                    sb.AppendLine($"  {text.Text}");
            }
        }

        return ToolResult.Ok(sb.Length > 0 ? sb.ToString() : "No messages in session.");
    }
}
