using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Clara.Adapters.Teams;

/// <summary>
/// SignalR client that connects to the Clara gateway hub.
/// Forwards messages between Teams and the gateway pipeline.
/// </summary>
public sealed class TeamsGatewayClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ILogger<TeamsGatewayClient> _logger;

    public event Func<string, string, Task>? OnTextDelta;
    public event Func<string, string, string, Task>? OnToolStatus;
    public event Func<string, Task>? OnComplete;
    public event Func<string, string, Task>? OnError;

    public TeamsGatewayClient(string gatewayUrl, string? secret, ILogger<TeamsGatewayClient> logger)
    {
        _logger = logger;

        _connection = new HubConnectionBuilder()
            .WithUrl($"{gatewayUrl}/hubs/adapter")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<string, string>("ReceiveTextDelta", (sessionKey, text) =>
            OnTextDelta?.Invoke(sessionKey, text) ?? Task.CompletedTask);

        _connection.On<string, string, string>("ReceiveToolStatus", (sessionKey, toolName, status) =>
            OnToolStatus?.Invoke(sessionKey, toolName, status) ?? Task.CompletedTask);

        _connection.On<string>("ReceiveComplete", (sessionKey) =>
            OnComplete?.Invoke(sessionKey) ?? Task.CompletedTask);

        _connection.On<string, string>("ReceiveError", (sessionKey, error) =>
            OnError?.Invoke(sessionKey, error) ?? Task.CompletedTask);

        _connection.Reconnecting += _ =>
        {
            _logger.LogWarning("Reconnecting to gateway...");
            return Task.CompletedTask;
        };

        _connection.Reconnected += _ =>
        {
            _logger.LogInformation("Reconnected to gateway");
            return Task.CompletedTask;
        };

        _connection.Closed += ex =>
        {
            _logger.LogWarning(ex, "Gateway connection closed");
            return Task.CompletedTask;
        };
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Connecting to gateway...");
        await _connection.StartAsync(ct);
        _logger.LogInformation("Connected to gateway");
    }

    public async Task AuthenticateAsync(string secret, CancellationToken ct = default)
    {
        await _connection.InvokeAsync("Authenticate", secret, ct);
        _logger.LogInformation("Authenticated with gateway");
    }

    public async Task SubscribeAsync(string sessionKey, CancellationToken ct = default)
    {
        await _connection.InvokeAsync("Subscribe", sessionKey, ct);
    }

    public async Task UnsubscribeAsync(string sessionKey, CancellationToken ct = default)
    {
        await _connection.InvokeAsync("Unsubscribe", sessionKey, ct);
    }

    public async Task SendMessageAsync(string sessionKey, string userId, string platform, string content, CancellationToken ct = default)
    {
        await _connection.InvokeAsync("SendMessage", sessionKey, userId, platform, content, ct);
    }

    public HubConnectionState State => _connection.State;

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
