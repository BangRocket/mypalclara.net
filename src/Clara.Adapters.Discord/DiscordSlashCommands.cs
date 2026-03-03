using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

namespace Clara.Adapters.Discord;

/// <summary>
/// Channel mode determines how Clara responds in a given channel.
/// </summary>
public enum ChannelMode
{
    /// <summary>Clara responds to all messages in the channel.</summary>
    Active,
    /// <summary>Clara only responds when mentioned.</summary>
    Mention,
    /// <summary>Clara does not respond in the channel.</summary>
    Off,
}

/// <summary>
/// Manages slash command registration and handling.
/// </summary>
public class DiscordSlashCommands
{
    private readonly ILogger<DiscordSlashCommands> _logger;
    private readonly ConcurrentDictionary<ulong, ChannelMode> _channelModes = new();

    public DiscordSlashCommands(ILogger<DiscordSlashCommands> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get the mode for a channel. Default is Mention for guild channels, Active for DMs.
    /// </summary>
    public ChannelMode GetChannelMode(DiscordChannel channel)
    {
        if (channel.IsPrivate) return ChannelMode.Active;
        return _channelModes.GetValueOrDefault(channel.Id, ChannelMode.Mention);
    }

    /// <summary>
    /// Set the mode for a channel.
    /// </summary>
    public void SetChannelMode(ulong channelId, ChannelMode mode)
    {
        _channelModes[channelId] = mode;
        _logger.LogInformation("Channel {ChannelId} mode set to {Mode}", channelId, mode);
    }

    /// <summary>
    /// Handle interaction created events (slash commands).
    /// </summary>
    public async Task HandleInteractionAsync(DiscordClient client, InteractionCreateEventArgs e)
    {
        if (e.Interaction.Type != InteractionType.ApplicationCommand)
            return;

        var name = e.Interaction.Data.Name;

        switch (name)
        {
            case "clara":
                await HandleClaraCommandAsync(e);
                break;
            default:
                _logger.LogDebug("Unknown slash command: {Name}", name);
                break;
        }
    }

    private async Task HandleClaraCommandAsync(InteractionCreateEventArgs e)
    {
        var options = e.Interaction.Data.Options;
        var subCommand = options?.FirstOrDefault();

        if (subCommand is null)
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("Usage: /clara mode [active|mention|off]"));
            return;
        }

        switch (subCommand.Name)
        {
            case "mode":
                var modeValue = subCommand.Options?.FirstOrDefault()?.Value?.ToString() ?? "mention";
                var mode = modeValue.ToLowerInvariant() switch
                {
                    "active" => ChannelMode.Active,
                    "mention" => ChannelMode.Mention,
                    "off" => ChannelMode.Off,
                    _ => ChannelMode.Mention,
                };

                SetChannelMode(e.Interaction.ChannelId, mode);

                await e.Interaction.CreateResponseAsync(
                    InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent($"Clara mode set to **{mode}** in this channel."));
                break;

            default:
                await e.Interaction.CreateResponseAsync(
                    InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent($"Unknown subcommand: {subCommand.Name}"));
                break;
        }
    }
}
