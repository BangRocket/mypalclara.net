using System.Text.Json;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Adapters.Discord;

/// <summary>
/// Slash commands for the Discord bot: /clara memory-search, /clara memory-key, /clara status, /clara model.
/// Commands that need LLM/memory state are forwarded to the Gateway via CommandRequest.
/// </summary>
[Group("clara", "Clara bot commands")]
public sealed class SlashCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly GatewayClient _gateway;
    private readonly MessageHandler _messageHandler;
    private readonly ClaraConfig _config;
    private readonly ILogger<SlashCommandModule> _logger;

    public SlashCommandModule(
        GatewayClient gateway,
        MessageHandler messageHandler,
        ClaraConfig config,
        ILogger<SlashCommandModule> logger)
    {
        _gateway = gateway;
        _messageHandler = messageHandler;
        _config = config;
        _logger = logger;
    }

    [SlashCommand("memory-search", "Search Clara's memories")]
    public async Task MemorySearchAsync(
        [Summary("query", "What to search for")] string query)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var args = new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement(query),
                ["userId"] = JsonSerializer.SerializeToElement($"discord-{Context.User.Id}"),
            };

            var result = await _gateway.CommandAsync(
                new CommandRequest("memory-search", args, $"discord-{Context.User.Id}"));

            if (!result.Success)
            {
                await FollowupAsync($"Memory search failed: {result.Error ?? "unknown error"}", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle($"Memory Search: {query}")
                .WithColor(Color.Purple)
                .WithCurrentTimestamp();

            if (result.Data.HasValue)
            {
                embed.WithDescription(FormatCommandData(result.Data.Value));
            }
            else
            {
                embed.WithDescription("No memories found.");
            }

            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory search failed");
            await FollowupAsync($"Memory search failed: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("memory-key", "Show key memories")]
    public async Task MemoryKeyAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var args = new Dictionary<string, JsonElement>
            {
                ["userId"] = JsonSerializer.SerializeToElement($"discord-{Context.User.Id}"),
            };

            var result = await _gateway.CommandAsync(
                new CommandRequest("memory-key", args, $"discord-{Context.User.Id}"));

            if (!result.Success)
            {
                await FollowupAsync($"Key memory fetch failed: {result.Error ?? "unknown error"}", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("Key Memories")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp();

            if (result.Data.HasValue)
            {
                embed.WithDescription(FormatCommandData(result.Data.Value));
            }
            else
            {
                embed.WithDescription("No key memories found.");
            }

            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Key memory fetch failed");
            await FollowupAsync($"Failed: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("status", "Show Clara's connection status")]
    public async Task StatusAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var result = await _gateway.CommandAsync(
                new CommandRequest("status", UserId: $"discord-{Context.User.Id}"));

            var embed = new EmbedBuilder()
                .WithTitle("Clara Status")
                .WithColor(Color.Green)
                .WithCurrentTimestamp();

            embed.AddField("Gateway", _gateway.IsConnected ? "Connected" : "Disconnected", inline: true);

            if (result.Success && result.Data.HasValue)
            {
                embed.AddField("Server Status", FormatCommandData(result.Data.Value));
            }
            else
            {
                embed.AddField("Server Status", result.Error ?? "Unknown", inline: true);
            }

            var tier = _messageHandler.GetChannelTier(Context.Channel.Id);
            embed.AddField("Channel Tier", tier ?? "default", inline: true);

            await FollowupAsync(embed: embed.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status command failed");
            await FollowupAsync($"Status check failed: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("model", "Set the model tier for this channel")]
    public async Task ModelAsync(
        [Summary("tier", "Model tier to use")]
        [Choice("High (Opus)", "high")]
        [Choice("Mid (Sonnet)", "mid")]
        [Choice("Low (Haiku)", "low")]
        [Choice("Default", "default")]
        string tier)
    {
        if (tier == "default")
        {
            _messageHandler.ClearChannelTier(Context.Channel.Id);
            await RespondAsync("Model tier reset to default for this channel.", ephemeral: true);
        }
        else
        {
            _messageHandler.SetChannelTier(Context.Channel.Id, tier);
            await RespondAsync($"Model tier set to **{tier}** for this channel.", ephemeral: true);
        }
    }

    private static string FormatCommandData(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.String)
            return data.GetString() ?? "";

        // Pretty-print JSON, truncated for Discord embed limits
        var text = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return text.Length > 4000 ? text[..3997] + "..." : text;
    }
}
