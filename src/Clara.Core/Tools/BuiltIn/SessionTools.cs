using System.Text;
using System.Text.Json;
using Clara.Core.Llm;
using Clara.Core.Sessions;
using Clara.Core.SubAgents;

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

public class SessionsSendTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "session_key": { "type": "string", "description": "Target session key" },
                "message": { "type": "string", "description": "Message to send to the session" }
            },
            "required": ["session_key", "message"]
        }
        """).RootElement;

    private readonly ISessionManager _sessionManager;

    public SessionsSendTool(ISessionManager sessionManager) => _sessionManager = sessionManager;

    public string Name => "sessions_send";
    public string Description => "Send a message to another session";
    public ToolCategory Category => ToolCategory.Session;
    public JsonElement ParameterSchema => Schema;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("session_key", out var keyEl) || keyEl.GetString() is not { } targetKey)
            return ToolResult.Fail("Missing required parameter: session_key");

        if (!arguments.TryGetProperty("message", out var msgEl) || msgEl.GetString() is not { } message)
            return ToolResult.Fail("Missing required parameter: message");

        var session = await _sessionManager.GetAsync(targetKey, ct);
        if (session is null)
            return ToolResult.Fail($"Session not found: {targetKey}");

        // Enqueue the message into the target session's message history
        session.Messages.Add(LlmMessage.User($"[Cross-session message from {context.SessionKey}]: {message}"));
        session.LastActivityAt = DateTime.UtcNow;
        await _sessionManager.UpdateAsync(session, ct);

        return ToolResult.Ok($"Message sent to session {targetKey}");
    }
}

public class SessionsSpawnTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "task": { "type": "string", "description": "The task for the sub-agent to perform" },
                "tier": { "type": "string", "enum": ["high", "mid", "low"], "description": "Model tier to use (default: low)" }
            },
            "required": ["task"]
        }
        """).RootElement;

    private readonly ISubAgentManager _subAgentManager;

    public SessionsSpawnTool(ISubAgentManager subAgentManager) => _subAgentManager = subAgentManager;

    public string Name => "sessions_spawn";
    public string Description => "Spawn a sub-agent for a background task";
    public ToolCategory Category => ToolCategory.Session;
    public JsonElement ParameterSchema => Schema;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("task", out var taskEl) || taskEl.GetString() is not { } task)
            return ToolResult.Fail("Missing required parameter: task");

        var tier = ModelTier.Low;
        if (arguments.TryGetProperty("tier", out var tierEl) && tierEl.GetString() is { } tierStr)
        {
            tier = tierStr.ToLowerInvariant() switch
            {
                "high" => ModelTier.High,
                "mid" => ModelTier.Mid,
                "low" => ModelTier.Low,
                _ => ModelTier.Low
            };
        }

        try
        {
            var request = new SubAgentRequest(task, context.SessionKey, tier);
            var subTaskId = await _subAgentManager.SpawnAsync(request, ct);
            return ToolResult.Ok($"Sub-agent spawned with ID: {subTaskId}. Use sessions_history to check its progress.");
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail(ex.Message);
        }
    }
}
