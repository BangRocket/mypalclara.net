using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using MyPalClara.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Core.Server;

/// <summary>
/// WebSocket connection handler for the Clara Gateway.
/// Manages the receive loop, message dispatch, and per-socket send serialization.
/// </summary>
public class GatewayServer
{
    private readonly NodeRegistry _nodeRegistry;
    private readonly ILogger<GatewayServer> _logger;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _sendLocks = new();

    private const int ReceiveBufferSize = 8192;

    /// <summary>Fired when a message request arrives from an adapter.</summary>
    public event Func<MessageRequest, WebSocket, string, Task>? OnMessageReceived;

    /// <summary>Fired when a cancel request arrives from an adapter.</summary>
    public event Func<CancelMessage, WebSocket, Task>? OnCancelReceived;

    /// <summary>Fired when an MCP management request arrives.</summary>
    public event Func<string, System.Text.Json.JsonElement, WebSocket, Task>? OnMcpRequest;

    public GatewayServer(NodeRegistry nodeRegistry, ILogger<GatewayServer> logger)
    {
        _nodeRegistry = nodeRegistry;
        _logger = logger;
    }

    /// <summary>The node registry for external access.</summary>
    public NodeRegistry NodeRegistry => _nodeRegistry;

    /// <summary>
    /// Handle a WebSocket connection from an adapter. Runs the receive loop until the socket closes.
    /// Called from ASP.NET middleware after WebSocket is accepted.
    /// </summary>
    public async Task HandleConnectionAsync(WebSocket ws, CancellationToken ct)
    {
        _sendLocks[ws] = new SemaphoreSlim(1, 1);
        _logger.LogInformation("WebSocket connection accepted");

        try
        {
            await ReceiveLoopAsync(ws, ct);
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            var node = _nodeRegistry.GetByWebSocket(ws);
            _logger.LogInformation("Adapter {NodeId} disconnected", node?.NodeId ?? "unknown");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("WebSocket connection cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in WebSocket connection handler");
        }
        finally
        {
            var node = _nodeRegistry.GetByWebSocket(ws);
            if (node != null)
            {
                _logger.LogInformation("Unregistering adapter {NodeId} ({Platform})", node.NodeId, node.Platform);
                _nodeRegistry.Unregister(ws);
            }

            if (_sendLocks.TryRemove(ws, out var semaphore))
            {
                semaphore.Dispose();
            }

            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
                catch
                {
                    // Best-effort close; ignore errors during cleanup.
                }
            }
        }
    }

    /// <summary>Send a protocol message to a specific WebSocket. Thread-safe via per-socket semaphore.</summary>
    public async Task SendAsync(WebSocket ws, object message, CancellationToken ct = default)
    {
        if (ws.State != WebSocketState.Open)
        {
            _logger.LogDebug("Skipping send to closed WebSocket");
            return;
        }

        var json = MessageParser.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        if (!_sendLocks.TryGetValue(ws, out var semaphore))
        {
            _logger.LogWarning("No send lock found for WebSocket; message dropped");
            return;
        }

        await semaphore.WaitAsync(ct);
        try
        {
            await ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>Send an error message to a WebSocket.</summary>
    public async Task SendErrorAsync(
        WebSocket ws,
        string? requestId,
        string code,
        string message,
        bool recoverable = true,
        CancellationToken ct = default)
    {
        var errorMsg = new ErrorMessage(code, message, requestId, recoverable);
        await SendAsync(ws, errorMsg, ct);
    }

    /// <summary>Broadcast a message to all nodes of a given platform.</summary>
    /// <returns>Number of nodes the message was sent to.</returns>
    public async Task<int> BroadcastToPlatformAsync(string platform, object message, CancellationToken ct = default)
    {
        var nodes = _nodeRegistry.GetByPlatform(platform);
        var sent = 0;

        foreach (var node in nodes)
        {
            try
            {
                await SendAsync(node.WebSocket, message, ct);
                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send broadcast to {NodeId}", node.NodeId);
            }
        }

        return sent;
    }

    private async Task ReceiveLoopAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[ReceiveBufferSize];
        var messageBuffer = new MemoryStream();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            messageBuffer.SetLength(0);

            // Accumulate fragments until EndOfMessage
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket close received");
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageBuffer.Write(buffer, 0, result.Count);
                }
            }
            while (!result.EndOfMessage);

            if (messageBuffer.Length == 0)
                continue;

            var json = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
            _logger.LogDebug("Received message: {Json}", json.Length > 200 ? json[..200] + "..." : json);

            try
            {
                await DispatchMessageAsync(ws, json, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching message");
                await SendErrorAsync(ws, null, "parse_error", $"Failed to process message: {ex.Message}", ct: ct);
            }
        }
    }

    private async Task DispatchMessageAsync(WebSocket ws, string json, CancellationToken ct)
    {
        var (type, data) = MessageParser.Parse(json);

        switch (type)
        {
            case MessageType.Register:
                await HandleRegisterAsync(ws, data, ct);
                break;

            case MessageType.Ping:
                await HandlePingAsync(ws, ct);
                break;

            case MessageType.Message:
                await HandleMessageAsync(ws, data, ct);
                break;

            case MessageType.Cancel:
                await HandleCancelAsync(ws, data, ct);
                break;

            case MessageType.Status:
                await HandleStatusAsync(ws, ct);
                break;

            // MCP management messages
            case MessageType.McpList:
            case MessageType.McpInstall:
            case MessageType.McpUninstall:
            case MessageType.McpStatus:
            case MessageType.McpRestart:
            case MessageType.McpEnable:
                await HandleMcpRequestAsync(ws, type, data, ct);
                break;

            default:
                _logger.LogWarning("Unknown message type: {Type}", type);
                await SendErrorAsync(ws, null, "unknown_type", $"Unknown message type: {type}", ct: ct);
                break;
        }
    }

    private async Task HandleRegisterAsync(WebSocket ws, System.Text.Json.JsonElement data, CancellationToken ct)
    {
        var msg = MessageParser.Deserialize<RegisterMessage>(data);
        var (sessionId, isReconnection) = _nodeRegistry.Register(
            ws, msg.NodeId, msg.Platform, msg.Capabilities, msg.Metadata);

        _logger.LogInformation(
            "Adapter {Action}: {NodeId} ({Platform}), session={SessionId}",
            isReconnection ? "reconnected" : "registered",
            msg.NodeId, msg.Platform, sessionId);

        var response = new RegisteredMessage(msg.NodeId, sessionId);
        await SendAsync(ws, response, ct);
    }

    private async Task HandlePingAsync(WebSocket ws, CancellationToken ct)
    {
        var node = _nodeRegistry.GetByWebSocket(ws);
        if (node != null)
        {
            node.LastPingAt = DateTime.UtcNow;
        }

        await SendAsync(ws, new PongMessage(), ct);
    }

    private async Task HandleMessageAsync(WebSocket ws, System.Text.Json.JsonElement data, CancellationToken ct)
    {
        var node = _nodeRegistry.GetByWebSocket(ws);
        if (node == null)
        {
            await SendErrorAsync(ws, null, "not_registered", "Must register before sending messages", ct: ct);
            return;
        }

        var msg = MessageParser.Deserialize<MessageRequest>(data);
        _logger.LogInformation(
            "Message from {NodeId}: user={UserId}, channel={ChannelId}, length={Length}",
            node.NodeId, msg.User.Id, msg.Channel.Id, msg.Content.Length);

        if (OnMessageReceived != null)
        {
            await OnMessageReceived.Invoke(msg, ws, node.Platform);
        }
        else
        {
            _logger.LogWarning("No message handler registered; dropping message {Id}", msg.Id);
            await SendErrorAsync(ws, msg.Id, "no_handler", "No message handler available", ct: ct);
        }
    }

    private async Task HandleCancelAsync(WebSocket ws, System.Text.Json.JsonElement data, CancellationToken ct)
    {
        var msg = MessageParser.Deserialize<CancelMessage>(data);
        _logger.LogInformation("Cancel request for {RequestId}", msg.RequestId);

        if (OnCancelReceived != null)
        {
            await OnCancelReceived.Invoke(msg, ws);
        }
        else
        {
            // Acknowledge even without a handler
            await SendAsync(ws, new CancelledMessage(msg.RequestId), ct);
        }
    }

    private async Task HandleStatusAsync(WebSocket ws, CancellationToken ct)
    {
        var uptimeSeconds = (int)(DateTime.UtcNow - _startedAt).TotalSeconds;
        var status = new StatusMessage(
            UptimeSeconds: uptimeSeconds);

        await SendAsync(ws, status, ct);
    }

    private async Task HandleMcpRequestAsync(
        WebSocket ws, string type, System.Text.Json.JsonElement data, CancellationToken ct)
    {
        if (OnMcpRequest != null)
        {
            await OnMcpRequest.Invoke(type, data, ws);
        }
        else
        {
            _logger.LogWarning("No MCP handler registered; dropping {Type} request", type);
            await SendErrorAsync(ws, null, "no_handler", $"MCP handler not available for {type}", ct: ct);
        }
    }
}
