using System.Collections.Concurrent;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Discord;

/// <summary>
/// Handles incoming Discord messages by forwarding them to the Gateway via GatewayClient.
/// Keeps: allowed server/channel filtering, stop phrases, active channel tracking,
/// tier prefix detection, per-channel SemaphoreSlim.
/// Removes: all direct LLM/MCP/memory/history dependencies (Gateway handles those).
/// </summary>
public sealed class MessageHandler
{
    private readonly GatewayClient _gateway;
    private readonly ClaraConfig _config;
    private readonly AttachmentHandler _attachmentHandler;
    private readonly ILogger<MessageHandler> _logger;

    // Per-channel concurrency control
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _channelLocks = new();

    // Per-channel tier overrides (set via /model slash command)
    private readonly ConcurrentDictionary<ulong, string> _channelTierOverrides = new();

    // Channels where Clara is actively engaged (not just responding to mentions)
    private readonly ConcurrentDictionary<ulong, DateTime> _activeChannels = new();

    // Tier prefix detection (same as ChatRepl)
    private static readonly Dictionary<string, string> TierPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["!high"] = "high", ["!opus"] = "high",
        ["!mid"] = "mid", ["!sonnet"] = "mid",
        ["!low"] = "low", ["!haiku"] = "low", ["!fast"] = "low",
    };

    private static readonly TimeSpan ActiveChannelTimeout = TimeSpan.FromMinutes(30);

    public MessageHandler(
        GatewayClient gateway,
        ClaraConfig config,
        AttachmentHandler attachmentHandler,
        ILogger<MessageHandler> logger)
    {
        _gateway = gateway;
        _config = config;
        _attachmentHandler = attachmentHandler;
        _logger = logger;
    }

    /// <summary>Set a tier override for a specific channel (from /model slash command).</summary>
    public void SetChannelTier(ulong channelId, string tier) =>
        _channelTierOverrides[channelId] = tier;

    /// <summary>Clear the tier override for a channel.</summary>
    public void ClearChannelTier(ulong channelId) =>
        _channelTierOverrides.TryRemove(channelId, out _);

    /// <summary>Get the current tier override for a channel, if any.</summary>
    public string? GetChannelTier(ulong channelId) =>
        _channelTierOverrides.TryGetValue(channelId, out var tier) ? tier : null;

    public async Task HandleAsync(SocketMessage rawMessage, SocketSelfUser self)
    {
        // Only process user messages
        if (rawMessage is not SocketUserMessage message) return;
        if (message.Author.IsBot) return;
        if (message.Author.Id == self.Id) return;

        var channelId = message.Channel.Id;
        var isDm = message.Channel is SocketDMChannel;
        var isMention = message.MentionedUsers.Any(u => u.Id == self.Id);

        // Check allowed servers/channels
        if (!isDm)
        {
            if (message.Channel is not SocketGuildChannel guildChannel) return;

            var allowedServers = _config.Discord.ParsedAllowedServers;
            if (allowedServers.Count > 0 && !allowedServers.Contains(guildChannel.Guild.Id))
                return;

            var allowedChannels = _config.Discord.ParsedAllowedChannels;
            if (allowedChannels.Count > 0 && !allowedChannels.Contains(channelId))
                return;
        }

        // Determine if we should respond
        var isActive = _activeChannels.TryGetValue(channelId, out var activeUntil)
                       && DateTime.UtcNow < activeUntil;

        if (!isDm && !isMention && !isActive)
            return;

        // Check stop phrases
        var stopPhrases = _config.Discord.ParsedStopPhrases;
        if (stopPhrases.Any(p => message.Content.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            _activeChannels.TryRemove(channelId, out _);
            await message.Channel.SendMessageAsync("Okay, I'll step back. Mention me if you need me again!");
            return;
        }

        // If mentioned in a server channel, mark as active
        if (!isDm && isMention)
            _activeChannels[channelId] = DateTime.UtcNow + ActiveChannelTimeout;

        // Serialize processing per channel
        var channelLock = _channelLocks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
        await channelLock.WaitAsync();
        try
        {
            await ProcessMessageAsync(message, self);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Discord message in channel {ChannelId}", channelId);
            try
            {
                await message.Channel.SendMessageAsync("Sorry, I ran into an error processing that. Please try again.");
            }
            catch { /* best effort */ }
        }
        finally
        {
            channelLock.Release();
        }
    }

    private async Task ProcessMessageAsync(SocketUserMessage message, SocketSelfUser self)
    {
        var channelId = message.Channel.Id;
        var content = message.CleanContent.Trim();

        // Strip bot mention from the beginning if present
        if (content.StartsWith($"@{self.Username}", StringComparison.OrdinalIgnoreCase))
            content = content[$"@{self.Username}".Length..].Trim();

        // Detect tier prefix
        string? tier = _channelTierOverrides.GetValueOrDefault(channelId);
        var firstSpace = content.IndexOf(' ');
        if (firstSpace > 0)
        {
            var prefix = content[..firstSpace];
            if (TierPrefixes.TryGetValue(prefix, out var detectedTier))
            {
                tier = detectedTier;
                content = content[(firstSpace + 1)..].Trim();
            }
        }

        if (string.IsNullOrEmpty(content) && message.Attachments.Count == 0)
            return;

        // Extract attachment content
        var attachmentText = await _attachmentHandler.ExtractAsync(message.Attachments);
        var attachments = !string.IsNullOrEmpty(attachmentText) ? new List<string> { attachmentText } : null;
        if (!string.IsNullOrEmpty(attachmentText))
            content = $"{attachmentText}\n\n{content}".Trim();

        // Determine channel metadata
        var channelType = message.Channel is SocketDMChannel ? "dm" : "server";
        var channelName = message.Channel is SocketGuildChannel gc ? $"#{gc.Name}" : "DM";
        var displayName = message.Author.GlobalName ?? message.Author.Username;

        // Build ChatRequest for Gateway
        var request = new ChatRequest(
            ChannelId: channelId.ToString(),
            ChannelName: channelName,
            ChannelType: channelType,
            UserId: $"discord-{message.Author.Id}",
            DisplayName: displayName,
            Content: content,
            Tier: tier,
            Attachments: attachments);

        // Trigger typing indicator
        using var typing = message.Channel.EnterTypingState();

        // Stream response from Gateway
        var fullText = "";
        await foreach (var response in _gateway.ChatAsync(request))
        {
            switch (response)
            {
                case TextChunk chunk:
                    fullText += chunk.Text;
                    break;
                case Complete complete:
                    fullText = complete.FullText;
                    break;
                case ErrorMessage error:
                    _logger.LogError("Gateway error: {Message}", error.Message);
                    fullText = $"Sorry, something went wrong: {error.Message}";
                    break;
            }
        }

        // Send response (split if >2000 chars)
        if (!string.IsNullOrEmpty(fullText))
        {
            var chunks = SplitMessage(fullText);
            foreach (var chunk in chunks)
            {
                await message.Channel.SendMessageAsync(
                    chunk,
                    messageReference: chunks.Count > 0 && chunk == chunks[0]
                        ? new global::Discord.MessageReference(message.Id)
                        : null);
            }
        }
    }

    /// <summary>
    /// Split a message into chunks of <=2000 chars.
    /// Tries to split at newlines, preserving code blocks.
    /// </summary>
    internal static List<string> SplitMessage(string text, int maxLength = 2000)
    {
        if (text.Length <= maxLength)
            return [text];

        var chunks = new List<string>();
        var remaining = text.AsSpan();

        while (remaining.Length > 0)
        {
            if (remaining.Length <= maxLength)
            {
                chunks.Add(remaining.ToString());
                break;
            }

            // Find a good split point
            var splitAt = maxLength;

            // Try to split at a newline
            var searchRange = remaining[..maxLength];
            var lastNewline = searchRange.LastIndexOf('\n');
            if (lastNewline > maxLength / 2)
                splitAt = lastNewline + 1;

            // Check if we're inside a code block
            var chunk = remaining[..splitAt].ToString();
            var backtickCount = chunk.Split("```").Length - 1;
            if (backtickCount % 2 == 1)
            {
                // We're inside a code block -- close it and reopen in next chunk
                chunk += "\n```";
                chunks.Add(chunk);
                remaining = remaining[splitAt..];
                // Reopen the code block
                var reopened = "```\n" + remaining.ToString();
                remaining = reopened.AsSpan();
            }
            else
            {
                chunks.Add(chunk);
                remaining = remaining[splitAt..];
            }
        }

        return chunks;
    }
}
