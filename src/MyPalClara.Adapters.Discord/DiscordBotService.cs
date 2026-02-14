using MyPalClara.Core.Configuration;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Adapters.Discord;

/// <summary>
/// IHostedService wrapping DiscordSocketClient lifecycle.
/// Starts the bot on StartAsync, stops on StopAsync.
/// No MCP dependency -- Gateway handles MCP now.
/// </summary>
public sealed class DiscordBotService : IHostedService, IAsyncDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly MessageHandler _messageHandler;
    private readonly ClaraConfig _config;
    private readonly IServiceProvider _services;
    private readonly ILogger<DiscordBotService> _logger;

    private readonly TaskCompletionSource _readyTcs = new();

    public DiscordBotService(
        MessageHandler messageHandler,
        ClaraConfig config,
        IServiceProvider services,
        ILogger<DiscordBotService> logger)
    {
        _messageHandler = messageHandler;
        _config = config;
        _services = services;
        _logger = logger;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds
                           | GatewayIntents.GuildMessages
                           | GatewayIntents.DirectMessages
                           | GatewayIntents.MessageContent,
            LogLevel = LogSeverity.Info,
            MessageCacheSize = 100,
        });

        _interactions = new InteractionService(_client.Rest, new InteractionServiceConfig
        {
            DefaultRunMode = RunMode.Async,
            LogLevel = LogSeverity.Info,
        });
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.Discord.BotToken))
        {
            _logger.LogError("Discord bot token not configured. Set Discord:BotToken in config.");
            throw new InvalidOperationException("Discord bot token is required.");
        }

        _client.Log += OnLog;
        _client.Ready += OnReady;
        _client.MessageReceived += msg =>
        {
            _ = Task.Run(() => _messageHandler.HandleAsync(msg, _client.CurrentUser));
            return Task.CompletedTask;
        };
        _client.InteractionCreated += OnInteractionCreated;

        await _client.LoginAsync(TokenType.Bot, _config.Discord.BotToken);
        await _client.StartAsync();

        // Wait for Ready event before registering commands
        await _readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Discord bot shutting down...");
        await _client.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _interactions.Dispose();
        await _client.DisposeAsync();
    }

    private async Task OnReady()
    {
        _logger.LogInformation("Logged in as {User} ({Id})", _client.CurrentUser, _client.CurrentUser.Id);

        // Register slash commands
        await _interactions.AddModulesAsync(typeof(DiscordBotService).Assembly, _services);

        // Register to allowed guilds if configured, otherwise globally
        var allowedServers = _config.Discord.ParsedAllowedServers;
        if (allowedServers.Count > 0)
        {
            foreach (var guildId in allowedServers)
            {
                await _interactions.RegisterCommandsToGuildAsync(guildId);
                _logger.LogInformation("Registered slash commands to guild {GuildId}", guildId);
            }
        }
        else
        {
            await _interactions.RegisterCommandsGloballyAsync();
            _logger.LogInformation("Registered slash commands globally");
        }

        _readyTcs.TrySetResult();
    }

    private async Task OnInteractionCreated(SocketInteraction interaction)
    {
        var ctx = new SocketInteractionContext(_client, interaction);
        await _interactions.ExecuteCommandAsync(ctx, _services);
    }

    private Task OnLog(LogMessage msg)
    {
        var level = msg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information,
        };

        _logger.Log(level, msg.Exception, "[Discord] {Source}: {Message}", msg.Source, msg.Message);
        return Task.CompletedTask;
    }
}
