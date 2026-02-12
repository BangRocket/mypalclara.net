using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Core.Protocol;

/// <summary>
/// WebSocket client used by all adapters to connect to the Gateway.
/// Handles authentication, sending requests, and receiving streamed responses.
/// </summary>
public sealed class GatewayClient : IAsyncDisposable
{
    private readonly Uri _gatewayUri;
    private readonly string _secret;
    private readonly string _adapterType;
    private readonly string _adapterId;
    private readonly ILogger<GatewayClient> _logger;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Channel of incoming responses from the Gateway.</summary>
    public Channel<GatewayResponse> Responses { get; } = Channel.CreateUnbounded<GatewayResponse>();

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public GatewayClient(Uri gatewayUri, string secret, string adapterType, string adapterId, ILogger<GatewayClient> logger)
    {
        _gatewayUri = gatewayUri;
        _secret = secret;
        _adapterType = adapterType;
        _adapterId = adapterId;
        _logger = logger;
    }

    /// <summary>Connect to the Gateway and authenticate.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_gatewayUri, ct);
        _logger.LogInformation("Connected to Gateway at {Uri}", _gatewayUri);

        // Start receiving
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveTask = Task.Run(() => ReceiveLoop(_receiveCts.Token), _receiveCts.Token);

        // Authenticate
        await SendAsync(new AuthMessage(_secret, _adapterType, _adapterId), ct);

        // Wait for auth result
        if (await Responses.Reader.WaitToReadAsync(ct) && Responses.Reader.TryRead(out var response))
        {
            if (response is AuthResult { Success: true })
            {
                _logger.LogInformation("Authenticated with Gateway as {Type}/{Id}", _adapterType, _adapterId);
            }
            else
            {
                throw new InvalidOperationException("Gateway authentication failed");
            }
        }
    }

    /// <summary>Send a chat request and yield streamed responses.</summary>
    public async IAsyncEnumerable<GatewayResponse> ChatAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await SendAsync(request, ct);

        await foreach (var response in Responses.Reader.ReadAllAsync(ct))
        {
            yield return response;

            if (response is Complete or ErrorMessage)
                break;
        }
    }

    /// <summary>Send a command request and get the result.</summary>
    public async Task<CommandResult> CommandAsync(CommandRequest request, CancellationToken ct = default)
    {
        await SendAsync(request, ct);

        await foreach (var response in Responses.Reader.ReadAllAsync(ct))
        {
            if (response is CommandResult result)
                return result;
            if (response is ErrorMessage error)
                return new CommandResult(request.Command, false, Error: error.Message);
        }

        return new CommandResult(request.Command, false, Error: "No response received");
    }

    private async Task SendAsync(AdapterMessage message, CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("Not connected to Gateway");

        var json = JsonSerializer.Serialize<AdapterMessage>(message, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[65536];

        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("Gateway closed connection");
                        Responses.Writer.TryComplete();
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());

                try
                {
                    var response = JsonSerializer.Deserialize<GatewayResponse>(json, JsonOpts);
                    if (response is not null)
                        await Responses.Writer.WriteAsync(response, ct);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize Gateway response");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket error in receive loop");
        }
        finally
        {
            Responses.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _receiveCts?.Cancel();

        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
            }
            catch { /* best effort */ }
        }

        if (_receiveTask is not null)
        {
            try { await _receiveTask; }
            catch { /* swallow cancellation */ }
        }

        _ws?.Dispose();
        _receiveCts?.Dispose();
    }
}
