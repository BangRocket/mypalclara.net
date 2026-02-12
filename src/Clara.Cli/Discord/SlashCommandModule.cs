using Clara.Core.Configuration;
using Clara.Core.Mcp;
using Clara.Core.Memory;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace Clara.Cli.Discord;

/// <summary>
/// Slash commands for the Discord bot: /memory, /status, /model.
/// </summary>
[Group("clara", "Clara bot commands")]
public sealed class SlashCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly MemoryService? _memory;
    private readonly McpServerManager _mcpManager;
    private readonly MessageHandler _messageHandler;
    private readonly ClaraConfig _config;
    private readonly ILogger<SlashCommandModule> _logger;

    public SlashCommandModule(
        McpServerManager mcpManager,
        MessageHandler messageHandler,
        ClaraConfig config,
        ILogger<SlashCommandModule> logger,
        MemoryService? memory = null)
    {
        _mcpManager = mcpManager;
        _messageHandler = messageHandler;
        _config = config;
        _logger = logger;
        _memory = memory;
    }

    [SlashCommand("memory-search", "Search Clara's memories")]
    public async Task MemorySearchAsync(
        [Summary("query", "What to search for")] string query)
    {
        await DeferAsync(ephemeral: true);

        if (_memory is null)
        {
            await FollowupAsync("Memory system is not available.", ephemeral: true);
            return;
        }

        try
        {
            var prefixedUserId = $"discord-{Context.User.Id}";
            var ctx = await _memory.FetchContextAsync(query, [prefixedUserId]);

            var embed = new EmbedBuilder()
                .WithTitle($"Memory Search: {query}")
                .WithColor(Color.Purple)
                .WithCurrentTimestamp();

            if (ctx.RelevantMemories.Count == 0 && ctx.KeyMemories.Count == 0)
            {
                embed.WithDescription("No memories found.");
            }
            else
            {
                if (ctx.KeyMemories.Count > 0)
                {
                    var keyText = string.Join("\n", ctx.KeyMemories.Take(5)
                        .Select(m => $"- {Truncate(m.Memory, 100)}"));
                    embed.AddField("Key Memories", keyText);
                }

                if (ctx.RelevantMemories.Count > 0)
                {
                    var relText = string.Join("\n", ctx.RelevantMemories.Take(5)
                        .Select(m => $"- [{m.Score:F2}] {Truncate(m.Memory, 100)}"));
                    embed.AddField("Relevant Memories", relText);
                }

                if (ctx.GraphRelations.Count > 0)
                {
                    var graphText = string.Join("\n", ctx.GraphRelations.Take(5)
                        .Select(g => $"- {Truncate(g, 100)}"));
                    embed.AddField("Graph Context", graphText);
                }
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

        if (_memory is null)
        {
            await FollowupAsync("Memory system is not available.", ephemeral: true);
            return;
        }

        try
        {
            var prefixedUserId = $"discord-{Context.User.Id}";
            var ctx = await _memory.FetchContextAsync("key memories overview", [prefixedUserId]);

            var embed = new EmbedBuilder()
                .WithTitle("Key Memories")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp();

            if (ctx.KeyMemories.Count == 0)
            {
                embed.WithDescription("No key memories found.");
            }
            else
            {
                foreach (var mem in ctx.KeyMemories.Take(10))
                {
                    embed.AddField(
                        Truncate(mem.Memory, 40),
                        $"Score: {mem.Score:F2} | Category: {mem.Category ?? "general"}",
                        inline: false);
                }
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
        var embed = new EmbedBuilder()
            .WithTitle("Clara Status")
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        embed.AddField("Provider", _config.Llm.Provider, inline: true);
        embed.AddField("Model", _config.Llm.ActiveProvider.Model, inline: true);

        var tools = _mcpManager.GetAllToolSchemas();
        embed.AddField("MCP Tools", $"{tools.Count} available", inline: true);

        embed.AddField("Memory", _memory is not null ? "Connected" : "Unavailable", inline: true);

        var tier = _messageHandler.GetChannelTier(Context.Channel.Id);
        embed.AddField("Channel Tier", tier ?? "default", inline: true);

        await RespondAsync(embed: embed.Build(), ephemeral: true);
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

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
}
