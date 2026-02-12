using System.Collections.Concurrent;
using System.Text;
using Clara.Core.Chat;
using Clara.Core.Configuration;
using Clara.Core.Identity;
using Clara.Core.Llm;
using Clara.Core.Mcp;
using Clara.Core.Memory;
using Clara.Core.Memory.Context;
using Clara.Core.Orchestration;
using Clara.Core.Personality;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Clara.Cli.Discord;

/// <summary>
/// Handles incoming Discord messages: filter → identity → memory → orchestrate → respond.
/// Serializes messages per-channel with SemaphoreSlim, parallelizes across channels.
/// </summary>
public sealed class MessageHandler
{
    private readonly ClaraConfig _config;
    private readonly LlmOrchestrator _orchestrator;
    private readonly McpServerManager _mcpManager;
    private readonly PersonalityLoader _personality;
    private readonly MemoryService? _memory;
    private readonly UserIdentityService? _identity;
    private readonly ChatHistoryService? _chatHistory;
    private readonly AttachmentHandler _attachmentHandler;
    private readonly ILogger<MessageHandler> _logger;

    // Per-channel concurrency control
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _channelLocks = new();

    // Per-channel conversation history
    private readonly ConcurrentDictionary<ulong, List<ChatMessage>> _channelHistory = new();

    // Per-channel conversation IDs (DB)
    private readonly ConcurrentDictionary<ulong, Guid> _channelConversationIds = new();

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

    private const int HistoryCharBudget = 200_000;
    private static readonly TimeSpan ActiveChannelTimeout = TimeSpan.FromMinutes(30);

    public MessageHandler(
        ClaraConfig config,
        LlmOrchestrator orchestrator,
        McpServerManager mcpManager,
        PersonalityLoader personality,
        AttachmentHandler attachmentHandler,
        ILogger<MessageHandler> logger,
        MemoryService? memory = null,
        UserIdentityService? identity = null,
        ChatHistoryService? chatHistory = null)
    {
        _config = config;
        _orchestrator = orchestrator;
        _mcpManager = mcpManager;
        _personality = personality;
        _attachmentHandler = attachmentHandler;
        _logger = logger;
        _memory = memory;
        _identity = identity;
        _chatHistory = chatHistory;
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
        if (!string.IsNullOrEmpty(attachmentText))
            content = $"{attachmentText}\n\n{content}".Trim();

        // Resolve identity
        var prefixedUserId = $"discord-{message.Author.Id}";
        IReadOnlyList<string> allUserIds = [prefixedUserId];
        Guid? userGuid = null;

        if (_identity is not null)
        {
            await _identity.EnsurePlatformLinkAsync(prefixedUserId, message.Author.GlobalName ?? message.Author.Username);
            allUserIds = await _identity.ResolveAllUserIdsAsync(prefixedUserId);
            userGuid = await _identity.ResolveUserGuidAsync(prefixedUserId, message.Author.GlobalName ?? message.Author.Username);
        }

        // Ensure DB channel + conversation
        Guid? conversationId = null;
        if (_chatHistory is not null && userGuid.HasValue)
        {
            var channelType = message.Channel is SocketDMChannel ? "dm" : "server";
            var channelName = message.Channel is SocketGuildChannel gc ? $"#{gc.Name}" : "DM";

            var channelResult = await _chatHistory.EnsureChannelAsync(
                "discord", "Discord",
                channelId.ToString(), channelName, channelType);

            if (channelResult.HasValue)
            {
                var (_, dbChannelId) = channelResult.Value;
                conversationId = await _chatHistory.GetOrCreateConversationAsync(dbChannelId, userGuid.Value);
                if (conversationId.HasValue)
                    _channelConversationIds[channelId] = conversationId.Value;
            }
        }

        // Load history from DB if we don't have it cached yet
        var history = _channelHistory.GetOrAdd(channelId, _ => []);
        if (history.Count == 0 && conversationId.HasValue && _chatHistory is not null)
        {
            var dbMessages = await _chatHistory.LoadRecentMessagesAsync(
                conversationId.Value, _config.Discord.MaxHistoryMessages);
            if (dbMessages.Count > 0)
                history.AddRange(dbMessages);
        }

        // Fetch memory context
        MemoryContext? memoryCtx = null;
        if (_memory is not null)
        {
            try
            {
                memoryCtx = await _memory.FetchContextAsync(content, allUserIds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Memory context fetch failed");
            }
        }

        // Build messages
        var messages = BuildMessages(content, history, memoryCtx);

        // Get tools
        var tools = _mcpManager.GetAllToolSchemas();

        // Add user message to history
        history.Add(new UserMessage(content));

        // Track sentiment
        _memory?.TrackSentiment(prefixedUserId, $"discord-{channelId}", content);

        // Trigger typing indicator
        using var typing = message.Channel.EnterTypingState();

        // Generate response
        var fullText = "";
        await foreach (var evt in _orchestrator.GenerateWithToolsAsync(messages, tools, tier))
        {
            switch (evt)
            {
                case TextChunkEvent chunk:
                    fullText += chunk.Text;
                    break;
                case CompleteEvent complete:
                    fullText = complete.FullText;
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

        // Add to history
        history.Add(new AssistantMessage(fullText));
        TrimHistory(history);

        // Background: persist exchange + memory
        if (conversationId.HasValue && !string.IsNullOrEmpty(fullText) && _chatHistory is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _chatHistory.StoreExchangeAsync(conversationId.Value, userGuid, content, fullText);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background chat history persist failed");
                }
            });
        }

        if (_memory is not null && !string.IsNullOrEmpty(fullText))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _memory.AddAsync(content, fullText, prefixedUserId);

                    if (memoryCtx is not null)
                    {
                        var usedIds = memoryCtx.RelevantMemories
                            .Concat(memoryCtx.KeyMemories)
                            .Select(m => m.Id);
                        await _memory.PromoteUsedMemoriesAsync(usedIds, allUserIds);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background memory processing failed");
                }
            });
        }
    }

    private List<ChatMessage> BuildMessages(string currentInput, List<ChatMessage> history, MemoryContext? memoryCtx)
    {
        var messages = new List<ChatMessage>();

        // System: personality
        messages.Add(new SystemMessage(_personality.GetPersonality()));

        // System: memory context sections
        if (memoryCtx is not null)
        {
            var sections = MemoryService.BuildPromptSections(memoryCtx);
            foreach (var section in sections)
                messages.Add(new SystemMessage(section));
        }

        // History
        messages.AddRange(history);

        // Current user message
        messages.Add(new UserMessage(currentInput));

        return messages;
    }

    private void TrimHistory(List<ChatMessage> history)
    {
        var maxMessages = _config.Discord.MaxHistoryMessages * 2;
        while (history.Count > maxMessages)
            history.RemoveAt(0);

        while (history.Count > 2)
        {
            var totalChars = 0;
            foreach (var msg in history)
                totalChars += msg.Content?.Length ?? 0;

            if (totalChars <= HistoryCharBudget)
                break;

            history.RemoveAt(0);
        }
    }

    /// <summary>
    /// Split a message into chunks of ≤2000 chars.
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
            var searchRange = remaining[..(maxLength)];
            var lastNewline = searchRange.LastIndexOf('\n');
            if (lastNewline > maxLength / 2)
                splitAt = lastNewline + 1;

            // Check if we're inside a code block
            var chunk = remaining[..splitAt].ToString();
            var backtickCount = chunk.Split("```").Length - 1;
            if (backtickCount % 2 == 1)
            {
                // We're inside a code block — close it and reopen in next chunk
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
