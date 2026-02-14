using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MyPalClara.Adapters.Telegram;

/// <summary>Handles incoming Telegram messages by routing through the Gateway.</summary>
public sealed class TelegramMessageHandler
{
    private readonly GatewayClient _gateway;
    private readonly ClaraConfig _config;
    private readonly ILogger<TelegramMessageHandler> _logger;

    public TelegramMessageHandler(
        GatewayClient gateway,
        ClaraConfig config,
        ILogger<TelegramMessageHandler> logger)
    {
        _gateway = gateway;
        _config = config;
        _logger = logger;
    }

    public async Task HandleMessageAsync(ITelegramBotClient client, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id.ToString();
        var chatName = message.Chat.Title ?? message.Chat.FirstName ?? chatId;
        var userId = message.From?.Id.ToString() ?? "unknown";
        var displayName = message.From is { } from
            ? $"{from.FirstName} {from.LastName}".Trim()
            : "Unknown";

        _logger.LogInformation("Message from {User} in {Chat}: {Text}",
            displayName, chatName, message.Text?[..Math.Min(message.Text.Length, 50)]);

        var request = new ChatRequest(
            ChannelId: chatId,
            ChannelName: chatName,
            ChannelType: message.Chat.Type == ChatType.Private ? "dm" : "group",
            UserId: userId,
            DisplayName: displayName,
            Content: message.Text!);

        var responseText = "";

        await foreach (var response in _gateway.ChatAsync(request, ct))
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

        // Split long messages (Telegram limit)
        var maxLen = _config.Telegram.MaxMessageLength;
        var chunks = SplitMessage(responseText, maxLen);

        foreach (var chunk in chunks)
        {
            await client.SendMessage(
                chatId: message.Chat.Id,
                text: chunk,
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
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

            // Find a good split point (newline or space)
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
