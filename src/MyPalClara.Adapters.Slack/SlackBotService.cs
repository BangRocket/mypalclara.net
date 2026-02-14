using MyPalClara.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlackNet;
using SlackNet.Events;
using SlackNet.WebApi;

namespace MyPalClara.Adapters.Slack;

/// <summary>Slack bot lifecycle managed as a hosted service using Socket Mode.</summary>
public sealed class SlackBotService : IHostedService
{
    private readonly ClaraConfig _config;
    private readonly SlackMessageHandler _handler;
    private readonly ILogger<SlackBotService> _logger;
    private ISlackSocketModeClient? _socketClient;

    public SlackBotService(
        ClaraConfig config,
        SlackMessageHandler handler,
        ILogger<SlackBotService> logger)
    {
        _config = config;
        _handler = handler;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var slackServices = new SlackServiceBuilder()
            .UseApiToken(_config.Slack.BotToken!)
            .UseAppLevelToken(_config.Slack.AppToken ?? "")
            .RegisterEventHandler<MessageEvent>(_handler);

        _socketClient = slackServices.GetSocketModeClient();

        await _socketClient.Connect();
        _logger.LogInformation("Slack bot connected via Socket Mode");
    }

    public Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Slack bot stopping...");
        _socketClient?.Disconnect();
        return Task.CompletedTask;
    }
}
