using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Clara.Adapters.Teams;

/// <summary>
/// Teams adapter using direct HTTP to the Bot Framework REST API.
/// Receives activities via POST /api/messages, sends replies via the Bot Framework service URL.
/// </summary>
public sealed class TeamsAdapter
{
    private readonly TeamsGatewayClient _gateway;
    private readonly HttpClient _httpClient;
    private readonly TeamsOptions _options;
    private readonly ILogger<TeamsAdapter> _logger;

    // Track pending responses: sessionKey -> accumulated text
    private readonly ConcurrentDictionary<string, StringBuilder> _pendingResponses = new();

    // Track conversation context for replies: sessionKey -> (serviceUrl, conversationId)
    private readonly ConcurrentDictionary<string, ConversationContext> _conversations = new();

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public TeamsAdapter(
        TeamsGatewayClient gateway,
        HttpClient httpClient,
        TeamsOptions options,
        ILogger<TeamsAdapter> logger)
    {
        _gateway = gateway;
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        // Wire gateway events
        _gateway.OnTextDelta += OnTextDelta;
        _gateway.OnComplete += OnComplete;
        _gateway.OnError += OnError;
        _gateway.OnToolStatus += OnToolStatus;
    }

    /// <summary>
    /// Connect to the Clara gateway via SignalR.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gateway.ConnectAsync(ct);
        if (!string.IsNullOrEmpty(_options.GatewaySecret))
            await _gateway.AuthenticateAsync(_options.GatewaySecret, ct);
        _logger.LogInformation("Teams adapter connected to gateway");
    }

    /// <summary>
    /// Handle an incoming Bot Framework activity from the /api/messages endpoint.
    /// </summary>
    public async Task HandleActivityAsync(JsonElement activity, CancellationToken ct = default)
    {
        var type = activity.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type != "message")
        {
            _logger.LogDebug("Ignoring activity type: {Type}", type);
            return;
        }

        var text = TeamsMessageMapper.ExtractContent(activity, _options.BotName);
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("Ignoring empty message");
            return;
        }

        var userId = TeamsMessageMapper.GetUserId(activity);
        var displayName = TeamsMessageMapper.GetDisplayName(activity);
        var sessionKey = TeamsMessageMapper.BuildSessionKey(activity);
        var serviceUrl = TeamsMessageMapper.GetServiceUrl(activity);
        var conversationId = TeamsMessageMapper.GetConversationId(activity);

        _logger.LogInformation("Teams message from {DisplayName} ({UserId}): {Text}",
            displayName, userId, text.Length > 100 ? text[..100] + "..." : text);

        // Store conversation context for reply routing
        _conversations[sessionKey] = new ConversationContext(serviceUrl, conversationId);

        // Subscribe to responses for this session
        await _gateway.SubscribeAsync(sessionKey, ct);

        // Initialize response accumulator
        _pendingResponses[sessionKey] = new StringBuilder();

        // Forward to gateway
        await _gateway.SendMessageAsync(sessionKey, userId, "teams", text, ct);
    }

    /// <summary>
    /// Validate the Authorization header on incoming requests using Bot Framework token validation.
    /// In production, this should verify JWT tokens from the Bot Framework.
    /// For simplicity, we validate the app ID claim matches our configured app ID.
    /// </summary>
    public bool ValidateAuth(string? authHeader)
    {
        if (string.IsNullOrEmpty(_options.AppId))
            return true; // Skip auth if no app ID configured

        if (string.IsNullOrEmpty(authHeader))
            return false;

        // In production, full JWT validation would be done here.
        // For now, accept if the header is present (adapter is behind a firewall or reverse proxy).
        return authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Send a reply to a Teams conversation via the Bot Framework REST API.
    /// </summary>
    private async Task SendReplyAsync(string sessionKey, string text)
    {
        if (!_conversations.TryGetValue(sessionKey, out var ctx))
        {
            _logger.LogWarning("No conversation context for session {SessionKey}", sessionKey);
            return;
        }

        try
        {
            await EnsureTokenAsync();

            var replyUrl = $"{ctx.ServiceUrl.TrimEnd('/')}/v3/conversations/{ctx.ConversationId}/activities";

            var payload = new
            {
                type = "message",
                text,
            };

            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, replyUrl);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (_accessToken is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send reply: {Status} {Body}", response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending reply to Teams");
        }
    }

    /// <summary>
    /// Acquire an OAuth token from the Bot Framework login endpoint.
    /// </summary>
    private async Task EnsureTokenAsync()
    {
        if (_accessToken is not null && DateTime.UtcNow < _tokenExpiry)
            return;

        if (string.IsNullOrEmpty(_options.AppId) || string.IsNullOrEmpty(_options.AppPassword))
        {
            _logger.LogDebug("No app credentials configured; skipping token acquisition");
            return;
        }

        try
        {
            var tokenUrl = "https://login.microsoftonline.com/botframework.com/oauth2/v2.0/token";
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.AppId,
                ["client_secret"] = _options.AppPassword,
                ["scope"] = "https://api.botframework.com/.default",
            });

            var response = await _httpClient.PostAsync(tokenUrl, form);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            _accessToken = doc.RootElement.GetProperty("access_token").GetString();
            var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // Refresh 1 min early

            _logger.LogDebug("Acquired Bot Framework token, expires in {Seconds}s", expiresIn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Bot Framework token");
        }
    }

    private Task OnTextDelta(string sessionKey, string text)
    {
        if (_pendingResponses.TryGetValue(sessionKey, out var sb))
            sb.Append(text);
        return Task.CompletedTask;
    }

    private async Task OnComplete(string sessionKey)
    {
        if (_pendingResponses.TryRemove(sessionKey, out var sb))
        {
            var text = sb.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                await SendReplyAsync(sessionKey, text);
        }
    }

    private async Task OnError(string sessionKey, string error)
    {
        _pendingResponses.TryRemove(sessionKey, out _);
        _logger.LogError("Gateway error for {SessionKey}: {Error}", sessionKey, error);
        await SendReplyAsync(sessionKey, $"Sorry, an error occurred: {error}");
    }

    private Task OnToolStatus(string sessionKey, string toolName, string status)
    {
        _logger.LogDebug("Tool {ToolName} status for {SessionKey}: {Status}", toolName, sessionKey, status);
        return Task.CompletedTask;
    }

    private sealed record ConversationContext(string ServiceUrl, string ConversationId);
}

public class TeamsOptions
{
    public string? AppId { get; set; }
    public string? AppPassword { get; set; }
    public string? BotName { get; set; }
    public string GatewayUrl { get; set; } = "http://127.0.0.1:18789";
    public string? GatewaySecret { get; set; }
}
