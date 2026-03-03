using Clara.Core.Config;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

namespace Clara.Adapters.Discord;

/// <summary>
/// Main Discord adapter. Connects to Discord via DSharpPlus and to the
/// Clara gateway via SignalR, bridging messages between them.
/// </summary>
public class DiscordAdapter : IAsyncDisposable
{
    private readonly DiscordClient _discordClient;
    private readonly DiscordGatewayClient _gatewayClient;
    private readonly DiscordResponseSender _responseSender;
    private readonly DiscordSlashCommands _slashCommands;
    private readonly DiscordImageHandler _imageHandler;
    private readonly DiscordOptions _options;
    private readonly ILogger<DiscordAdapter> _logger;

    private ulong _botUserId;

    public DiscordAdapter(
        DiscordClient discordClient,
        DiscordGatewayClient gatewayClient,
        DiscordResponseSender responseSender,
        DiscordSlashCommands slashCommands,
        DiscordImageHandler imageHandler,
        DiscordOptions options,
        ILogger<DiscordAdapter> logger)
    {
        _discordClient = discordClient;
        _gatewayClient = gatewayClient;
        _responseSender = responseSender;
        _slashCommands = slashCommands;
        _imageHandler = imageHandler;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        // Wire up Discord events
        _discordClient.MessageCreated += OnMessageCreatedAsync;
        _discordClient.InteractionCreated += OnInteractionCreatedAsync;

        // Connect to gateway first
        await _gatewayClient.ConnectAsync(ct);
        _logger.LogInformation("Connected to Clara gateway");

        // Wire up gateway response events
        _gatewayClient.OnTextDelta += async (sessionKey, text) =>
            await _responseSender.AppendTextAsync(sessionKey, text);

        _gatewayClient.OnToolStatus += async (sessionKey, toolName, status) =>
            await _responseSender.ShowToolStatusAsync(sessionKey, toolName, status);

        _gatewayClient.OnComplete += async (sessionKey) =>
            await _responseSender.CompleteAsync(sessionKey);

        _gatewayClient.OnError += async (sessionKey, error) =>
            await _responseSender.SendErrorAsync(sessionKey, error);

        // Connect to Discord
        await _discordClient.ConnectAsync();
        _botUserId = _discordClient.CurrentUser.Id;
        _logger.LogInformation("Connected to Discord as {BotName} ({BotId})",
            _discordClient.CurrentUser.Username, _botUserId);
    }

    private async Task OnMessageCreatedAsync(DiscordClient client, MessageCreateEventArgs e)
    {
        // Ignore bot messages
        if (e.Author.IsBot) return;

        // Ignore messages from disallowed servers
        if (e.Guild is not null && _options.AllowedServers.Count > 0
            && !_options.AllowedServers.Contains(e.Guild.Id.ToString()))
            return;

        // Check channel mode
        var mode = _slashCommands.GetChannelMode(e.Channel);
        switch (mode)
        {
            case ChannelMode.Off:
                return;
            case ChannelMode.Mention when !IsMentioned(e):
                return;
        }

        var content = DiscordMessageMapper.ExtractContent(e.Message, _botUserId);
        if (string.IsNullOrWhiteSpace(content)) return;

        var sessionKey = DiscordMessageMapper.BuildSessionKey(e);
        var userId = e.Author.Id.ToString();

        _logger.LogInformation("Message from {User} in {SessionKey}: {ContentLength} chars",
            DiscordMessageMapper.GetDisplayName(e.Author), sessionKey, content.Length);

        // Register channel for response routing
        _responseSender.RegisterChannel(sessionKey, e.Channel);

        // Subscribe to this session's responses
        await _gatewayClient.SubscribeAsync(sessionKey);

        // Show typing indicator
        await e.Channel.TriggerTypingAsync();

        // Forward to gateway
        await _gatewayClient.SendMessageAsync(sessionKey, userId, "discord", content);
    }

    private async Task OnInteractionCreatedAsync(DiscordClient client, InteractionCreateEventArgs e)
    {
        await _slashCommands.HandleInteractionAsync(client, e);
    }

    private bool IsMentioned(MessageCreateEventArgs e)
    {
        return e.MentionedUsers.Any(u => u.Id == _botUserId)
            || e.Channel.IsPrivate;
    }

    public async ValueTask DisposeAsync()
    {
        _discordClient.MessageCreated -= OnMessageCreatedAsync;
        _discordClient.InteractionCreated -= OnInteractionCreatedAsync;

        await _gatewayClient.DisposeAsync();
        await _discordClient.DisconnectAsync();
        _discordClient.Dispose();
    }
}
