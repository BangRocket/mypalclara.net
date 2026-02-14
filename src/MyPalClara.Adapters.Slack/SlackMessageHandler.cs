using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using Microsoft.Extensions.Logging;
using SlackNet;
using SlackNet.Events;

namespace MyPalClara.Adapters.Slack;

/// <summary>Handles incoming Slack messages by routing through the Gateway.</summary>
public sealed class SlackMessageHandler : IEventHandler<MessageEvent>
{
    private readonly GatewayClient _gateway;
    private readonly ClaraConfig _config;
    private readonly ILogger<SlackMessageHandler> _logger;

    public SlackMessageHandler(
        GatewayClient gateway,
        ClaraConfig config,
        ILogger<SlackMessageHandler> logger)
    {
        _gateway = gateway;
        _config = config;
        _logger = logger;
    }

    public async Task Handle(MessageEvent message)
    {
        // Ignore bot messages
        if (!string.IsNullOrEmpty(message.BotId) || string.IsNullOrEmpty(message.Text))
            return;

        var channelId = message.Channel;

        // Check allowed channels (empty = allow all)
        if (_config.Slack.AllowedChannels.Count > 0 &&
            !_config.Slack.AllowedChannels.Contains(channelId))
            return;

        var userId = message.User ?? "unknown";
        _logger.LogInformation("Slack message from {User} in {Channel}", userId, channelId);

        var request = new ChatRequest(
            ChannelId: channelId,
            ChannelName: channelId,
            ChannelType: message.ChannelType ?? "channel",
            UserId: userId,
            DisplayName: userId,
            Content: message.Text);

        var responseText = "";

        await foreach (var response in _gateway.ChatAsync(request))
        {
            switch (response)
            {
                case TextChunk chunk:
                    responseText += chunk.Text;
                    break;

                case Complete complete:
                    responseText = complete.FullText;
                    break;

                case ErrorMessage error:
                    responseText = $"Error: {error.Message}";
                    break;
            }
        }

        if (string.IsNullOrEmpty(responseText)) return;

        // Split long messages
        var maxLen = _config.Slack.MaxMessageLength;
        var chunks = SplitMessage(responseText, maxLen);

        var api = new SlackServiceBuilder()
            .UseApiToken(_config.Slack.BotToken!)
            .GetApiClient();

        foreach (var chunk in chunks)
        {
            await api.Chat.PostMessage(new SlackNet.WebApi.Message
            {
                Channel = channelId,
                Text = chunk,
            });
        }
    }

    private static List<string> SplitMessage(string text, int maxLen)
    {
        if (text.Length <= maxLen)
            return [text];

        var parts = new List<string>();
        var remaining = text.AsSpan();

        while (remaining.Length > 0)
        {
            if (remaining.Length <= maxLen)
            {
                parts.Add(remaining.ToString());
                break;
            }

            var splitAt = remaining[..maxLen].LastIndexOf('\n');
            if (splitAt < maxLen / 2)
                splitAt = remaining[..maxLen].LastIndexOf(' ');
            if (splitAt < maxLen / 4)
                splitAt = maxLen;

            parts.Add(remaining[..splitAt].ToString());
            remaining = remaining[splitAt..].TrimStart("\n ");
        }

        return parts;
    }
}
