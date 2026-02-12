using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyPalClara.Core.Chat;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Identity;
using MyPalClara.Core.Llm;
using MyPalClara.Core.Memory;
using MyPalClara.Core.Orchestration;
using MyPalClara.Core.Personality;
using MyPalClara.Core.Protocol;
using MyPalClara.Gateway.Mcp;
using MyPalClara.Gateway.Orchestration;
using MyPalClara.Gateway.Sessions;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.WebSocket;

/// <summary>
/// Routes incoming WebSocket messages from adapters to the appropriate handler.
/// Handles authentication, chat requests, and command requests.
/// </summary>
public sealed class MessageRouter
{
    private readonly ClaraConfig _config;
    private readonly ConnectionManager _connections;
    private readonly UserIdentityService _identity;
    private readonly ChatHistoryService _chatHistory;
    private readonly PersonalityLoader _personality;
    private readonly LlmOrchestrator _orchestrator;
    private readonly McpServerManager _mcp;
    private readonly IMemoryService? _memory;
    private readonly ILogger<MessageRouter> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MessageRouter(
        ClaraConfig config,
        ConnectionManager connections,
        UserIdentityService identity,
        ChatHistoryService chatHistory,
        PersonalityLoader personality,
        LlmOrchestrator orchestrator,
        McpServerManager mcp,
        IMemoryService? memory,
        ILogger<MessageRouter> logger)
    {
        _config = config;
        _connections = connections;
        _identity = identity;
        _chatHistory = chatHistory;
        _personality = personality;
        _orchestrator = orchestrator;
        _mcp = mcp;
        _memory = memory;
        _logger = logger;
    }

    /// <summary>Handle a new WebSocket connection — read messages until close.</summary>
    public async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket ws, string connectionId, CancellationToken ct)
    {
        AdapterSession? session = null;
        var buffer = new byte[65536];

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (session is not null)
                            _connections.Remove(connectionId);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());

                try
                {
                    var message = JsonSerializer.Deserialize<AdapterMessage>(json, JsonOpts);
                    if (message is null) continue;

                    switch (message)
                    {
                        case AuthMessage auth:
                            session = await HandleAuthAsync(ws, connectionId, auth, ct);
                            break;

                        case ChatRequest chat when session is not null:
                            // Fire and forget so we don't block the receive loop
                            _ = Task.Run(() => HandleChatAsync(session, chat, ct), ct);
                            break;

                        case CommandRequest cmd when session is not null:
                            _ = Task.Run(() => HandleCommandAsync(session, cmd, ct), ct);
                            break;

                        default:
                            _logger.LogWarning("Unexpected message type or unauthenticated: {Type}", message.GetType().Name);
                            break;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize adapter message");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket error for connection {ConnId}", connectionId);
        }
        finally
        {
            if (session is not null)
                _connections.Remove(connectionId);
        }
    }

    private async Task<AdapterSession?> HandleAuthAsync(
        System.Net.WebSockets.WebSocket ws, string connectionId, AuthMessage auth, CancellationToken ct)
    {
        var valid = auth.Secret == _config.Gateway.Secret;

        if (!valid)
        {
            _logger.LogWarning("Authentication failed for {Type}/{Id}", auth.AdapterType, auth.AdapterId);
            await SendAsync(ws, new AuthResult(false), ct);
            return null;
        }

        var session = new AdapterSession(connectionId, auth.AdapterType, auth.AdapterId, ws);
        _connections.Add(session);
        await SendAsync(ws, new AuthResult(true), ct);
        return session;
    }

    private async Task HandleChatAsync(AdapterSession session, ChatRequest chat, CancellationToken ct)
    {
        try
        {
            // 1. Get or create channel session
            var channelSession = session.GetOrCreateChannel(chat.ChannelId, chat.ChannelName, chat.ChannelType);

            // 2. Resolve user identity
            var prefixedUserId = $"{session.AdapterType}-{chat.UserId}";
            var userGuid = await _identity.ResolveUserGuidAsync(prefixedUserId, chat.DisplayName);
            channelSession.UserGuid = userGuid;

            // 3. Ensure DB channel + conversation
            if (userGuid is not null && channelSession.ConversationId is null)
            {
                var channelResult = await _chatHistory.EnsureChannelAsync(
                    session.AdapterType, session.AdapterType.ToUpperInvariant(),
                    chat.ChannelId, chat.ChannelName, chat.ChannelType, ct);

                if (channelResult is not null)
                {
                    channelSession.ConversationId = await _chatHistory.GetOrCreateConversationAsync(
                        channelResult.Value.ChannelId, userGuid.Value, ct);
                }
            }

            // 4. Fetch memory context
            MemoryContext? memoryContext = null;
            if (_memory is not null)
            {
                var allUserIds = await _identity.ResolveAllUserIdsAsync(prefixedUserId);
                memoryContext = await _memory.FetchContextAsync(chat.Content, allUserIds, ct);
            }

            // 5. Build messages
            var messages = new List<ChatMessage>();

            // Personality
            var personality = _personality.GetPersonality();
            messages.Add(new SystemMessage(personality));

            // Memory sections
            if (_memory is not null && memoryContext is not null)
            {
                var memorySections = _memory.BuildPromptSections(memoryContext);
                foreach (var section in memorySections)
                {
                    if (!string.IsNullOrEmpty(section))
                        messages.Add(new SystemMessage(section));
                }
            }

            // History from DB (if available)
            if (channelSession.ConversationId is not null)
            {
                var dbHistory = await _chatHistory.LoadRecentMessagesAsync(
                    channelSession.ConversationId.Value, ct: ct);
                messages.AddRange(dbHistory);
            }
            else
            {
                // Use in-memory history
                messages.AddRange(channelSession.History);
            }

            // User message
            messages.Add(new UserMessage(chat.Content));

            // 6. Get MCP tool schemas
            var tools = _mcp.GetAllToolSchemas();

            // 7. Stream orchestrator events → WebSocket responses
            var fullText = "";
            var toolCount = 0;

            await foreach (var evt in _orchestrator.GenerateWithToolsAsync(
                messages, tools, chat.Tier, ct: ct))
            {
                switch (evt)
                {
                    case TextChunkEvent textChunk:
                        await SendAsync(session.WebSocket, new TextChunk(textChunk.Text), ct);
                        break;

                    case ToolStartEvent toolStart:
                        await SendAsync(session.WebSocket, new ToolStart(toolStart.ToolName, toolStart.Step), ct);
                        break;

                    case ToolResultEvent toolResult:
                        await SendAsync(session.WebSocket, new ToolResult(toolResult.ToolName, toolResult.Success, toolResult.OutputPreview), ct);
                        break;

                    case CompleteEvent complete:
                        fullText = complete.FullText;
                        toolCount = complete.ToolCount;
                        await SendAsync(session.WebSocket, new Complete(complete.FullText, complete.ToolCount), ct);
                        break;
                }
            }

            // 8. Update session history
            channelSession.AddMessages(
                new UserMessage(chat.Content),
                new AssistantMessage(fullText));

            // 9. Background: persist chat + memory
            if (channelSession.ConversationId is not null)
            {
                _ = _chatHistory.StoreExchangeAsync(
                    channelSession.ConversationId.Value,
                    channelSession.UserGuid,
                    chat.Content, fullText, ct);
            }

            if (_memory is not null && !string.IsNullOrWhiteSpace(fullText))
            {
                _ = _memory.AddAsync(chat.Content, fullText, prefixedUserId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling chat request for {Channel}", chat.ChannelId);
            await SendAsync(session.WebSocket, new ErrorMessage($"Internal error: {ex.Message}"), ct);
        }
    }

    private async Task HandleCommandAsync(AdapterSession session, CommandRequest cmd, CancellationToken ct)
    {
        try
        {
            var result = cmd.Command.ToLowerInvariant() switch
            {
                "status" => HandleStatusCommand(),
                "mcp-status" => HandleMcpStatusCommand(),
                _ => new CommandResult(cmd.Command, false, Error: $"Unknown command: {cmd.Command}"),
            };

            await SendAsync(session.WebSocket, result, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command {Command}", cmd.Command);
            await SendAsync(session.WebSocket,
                new CommandResult(cmd.Command, false, Error: ex.Message), ct);
        }
    }

    private CommandResult HandleStatusCommand()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            connections = _connections.Count,
            model = _config.Llm.ActiveProvider.Model,
            provider = _config.Llm.Provider,
            memoryEnabled = _memory is not null,
        });
        return new CommandResult("status", true, Data: data);
    }

    private CommandResult HandleMcpStatusCommand()
    {
        var status = _mcp.GetServerStatus();
        var data = JsonSerializer.SerializeToElement(status);
        return new CommandResult("mcp-status", true, Data: data);
    }

    private static async Task SendAsync(System.Net.WebSockets.WebSocket ws, GatewayResponse response, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize<GatewayResponse>(response, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }
}
