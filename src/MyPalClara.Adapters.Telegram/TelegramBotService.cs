using MyPalClara.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MyPalClara.Adapters.Telegram;

/// <summary>Telegram bot lifecycle managed as a hosted service.</summary>
public sealed class TelegramBotService : IHostedService
{
    private readonly ClaraConfig _config;
    private readonly TelegramMessageHandler _handler;
    private readonly ILogger<TelegramBotService> _logger;
    private TelegramBotClient? _client;
    private CancellationTokenSource? _cts;

    public TelegramBotService(
        ClaraConfig config,
        TelegramMessageHandler handler,
        ILogger<TelegramBotService> logger)
    {
        _config = config;
        _handler = handler;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _client = new TelegramBotClient(_config.Telegram.Token!);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var me = await _client.GetMe(ct);
        _logger.LogInformation("Telegram bot started: @{Username} ({Id})", me.Username, me.Id);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message],
        };

        _client.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: _cts.Token);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Telegram bot stopping...");
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        if (update.Message is not { Text: { } text } message)
            return;

        // Check allowed chat IDs (empty = allow all)
        if (_config.Telegram.AllowedChatIds.Count > 0 &&
            !_config.Telegram.AllowedChatIds.Contains(message.Chat.Id))
        {
            _logger.LogDebug("Ignoring message from unauthorized chat {ChatId}", message.Chat.Id);
            return;
        }

        await _handler.HandleMessageAsync(client, message, ct);
    }

    private Task HandleErrorAsync(ITelegramBotClient client, Exception ex, HandleErrorSource source, CancellationToken ct)
    {
        _logger.LogError(ex, "Telegram polling error ({Source})", source);
        return Task.CompletedTask;
    }
}
