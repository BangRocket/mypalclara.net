using Clara.Core.Chat;
using Clara.Core.Configuration;
using Clara.Core.Identity;
using Clara.Core.Memory;
using Clara.Tools.Backfill.Models;
using Clara.Tools.Backfill.Parsers;
using Microsoft.Extensions.Logging;

namespace Clara.Tools.Backfill;

/// <summary>
/// Orchestrates chat history backfill: parse → sort → create adapters/channels → process exchanges.
/// </summary>
public sealed class BackfillRunner
{
    private readonly ClaraConfig _config;
    private readonly ChatHistoryService _chatHistory;
    private readonly MemoryService _memory;
    private readonly UserIdentityService _identity;
    private readonly ILogger<BackfillRunner> _logger;

    public BackfillRunner(
        ClaraConfig config,
        ChatHistoryService chatHistory,
        MemoryService memory,
        UserIdentityService identity,
        ILogger<BackfillRunner> logger)
    {
        _config = config;
        _chatHistory = chatHistory;
        _memory = memory;
        _identity = identity;
        _logger = logger;
    }

    public async Task RunAsync(BackfillOptions options, CancellationToken ct = default)
    {
        var chatsDir = options.ChatsDir;
        if (!Directory.Exists(chatsDir))
        {
            _logger.LogError("Chats directory not found: {Dir}", chatsDir);
            return;
        }

        // Parse all sources
        _logger.LogInformation("Parsing chat exports from {Dir}...", chatsDir);
        var conversations = ParseAll(chatsDir, options.Source);

        // Sort by first exchange timestamp
        conversations = conversations
            .Where(c => c.Exchanges.Count > 0)
            .OrderBy(c => c.Exchanges[0].UserTimestamp)
            .ToList();

        // Print stats
        var totalExchanges = conversations.Sum(c => c.Exchanges.Count);
        _logger.LogInformation("Parsed {ConvCount} conversations, {ExchangeCount} exchanges total",
            conversations.Count, totalExchanges);

        var bySource = conversations.GroupBy(c => c.SourceType);
        foreach (var group in bySource)
        {
            var exchangeCount = group.Sum(c => c.Exchanges.Count);
            _logger.LogInformation("  {Source}: {ConvCount} conversations, {ExchangeCount} exchanges",
                group.Key, group.Count(), exchangeCount);
        }

        if (options.DryRun)
        {
            _logger.LogInformation("Dry run — no data written. Listing conversations:");
            foreach (var conv in conversations)
            {
                var first = conv.Exchanges[0].UserTimestamp;
                var last = conv.Exchanges[^1].AssistantTimestamp;
                _logger.LogInformation("  [{Source}] {Title}: {Count} exchanges, {First:yyyy-MM-dd} to {Last:yyyy-MM-dd}",
                    conv.SourceType, conv.Title, conv.Exchanges.Count, first, last);
            }
            return;
        }

        // Load or create checkpoint
        var checkpointPath = Path.Combine(chatsDir, ".backfill-checkpoint.json");
        var checkpoint = new BackfillCheckpoint(checkpointPath);

        if (options.ResetCheckpoint)
        {
            checkpoint.Reset();
            _logger.LogInformation("Checkpoint reset");
        }

        var skipped = conversations.Count(c => checkpoint.IsCompleted(c.SourceId));
        if (skipped > 0)
            _logger.LogInformation("Skipping {Count} already-completed conversations", skipped);

        // Resolve user identity
        var userId = options.UserId ?? _config.UserId;
        await _identity.EnsurePlatformLinkAsync(userId);
        var userGuid = await _identity.ResolveUserGuidAsync(userId);
        if (userGuid is null)
        {
            _logger.LogError("Failed to resolve user GUID for {UserId}", userId);
            return;
        }

        // Process each conversation
        var processed = 0;
        var failed = 0;
        var totalConvs = conversations.Count(c => !checkpoint.IsCompleted(c.SourceId));
        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("Beginning backfill of {Total} conversations (skip-history={SkipHistory}, skip-memory={SkipMemory})",
            totalConvs, options.SkipHistory, options.SkipMemory);

        foreach (var conv in conversations)
        {
            if (ct.IsCancellationRequested) break;
            if (checkpoint.IsCompleted(conv.SourceId)) continue;

            processed++;
            _logger.LogInformation("[{Current}/{Total}] Processing {Source}: {Title} ({Count} exchanges)",
                processed, totalConvs, conv.SourceType, conv.Title, conv.Exchanges.Count);

            try
            {
                await ProcessConversationAsync(conv, userGuid.Value, userId, options, ct);
                checkpoint.MarkCompleted(conv.SourceId);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Failed to process conversation {SourceId}, skipping", conv.SourceId);
            }
        }

        totalSw.Stop();
        var totalExchangesProcessed = conversations
            .Where(c => !checkpoint.IsCompleted(c.SourceId) is false)
            .Take(processed)
            .Sum(c => c.Exchanges.Count);

        _logger.LogInformation("Backfill complete in {Elapsed:F1}s: {Processed} conversations processed, {Failed} failed, {Skipped} skipped (checkpoint)",
            totalSw.Elapsed.TotalSeconds, processed, failed, skipped);
    }

    private async Task ProcessConversationAsync(
        BackfillConversation conv, Guid userGuid, string userId,
        BackfillOptions options, CancellationToken ct)
    {
        // Determine adapter type and channel name
        var (adapterType, adapterName, externalId, channelName, channelType) = conv.SourceType switch
        {
            "chatgpt" => ("chatgpt", "ChatGPT (Mara)", $"chatgpt-{conv.SourceId}", conv.Title, "dm"),
            "discord-dm" => ("discord", "Discord", "discord-dm-1453236645993254952", "DM with MyPalClara", "dm"),
            "discord-server" => ("discord", "Discord", "discord-server-1451983877244715241", "#clara (JORSHTOPIA)", "text"),
            _ => throw new ArgumentException($"Unknown source type: {conv.SourceType}")
        };

        var dateRange = $"{conv.Exchanges[0].UserTimestamp:yyyy-MM-dd} to {conv.Exchanges[^1].AssistantTimestamp:yyyy-MM-dd}";
        _logger.LogInformation("  Source: {Source}, Channel: {Channel}, Dates: {Range}",
            adapterType, channelName, dateRange);

        // Create adapter/channel
        Guid channelId;
        if (!options.SkipHistory)
        {
            _logger.LogInformation("  Ensuring channel: {Adapter}/{Channel}", adapterName, channelName);
            var channelResult = await _chatHistory.EnsureChannelAsync(
                adapterType, adapterName, externalId, channelName, channelType, ct);
            if (channelResult is null)
            {
                _logger.LogWarning("Failed to ensure channel for {SourceId}", conv.SourceId);
                return;
            }
            channelId = channelResult.Value.ChannelId;
            _logger.LogDebug("  Channel ID: {ChannelId}", channelId);
        }
        else
        {
            channelId = Guid.Empty;
            _logger.LogInformation("  Skipping history (--skip-history)");
        }

        // Create conversation with historical timestamp
        Guid? conversationId = null;
        if (!options.SkipHistory)
        {
            conversationId = await _chatHistory.CreateBackfillConversationAsync(
                channelId, userGuid,
                conv.Exchanges[0].UserTimestamp,
                ct);
            _logger.LogDebug("  Conversation ID: {ConversationId}", conversationId);
        }

        // Process each exchange
        var emotionalChannelId = $"{adapterType}-{externalId}";
        var memorySuccessCount = 0;
        var memoryFailCount = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < conv.Exchanges.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var exchange = conv.Exchanges[i];

            if (i % 10 == 0 || i == conv.Exchanges.Count - 1)
            {
                _logger.LogInformation("  Exchange {Current}/{Total} ({Date:yyyy-MM-dd HH:mm}): \"{Preview}\"",
                    i + 1, conv.Exchanges.Count, exchange.UserTimestamp,
                    exchange.UserMessage.Length > 60 ? exchange.UserMessage[..60] + "..." : exchange.UserMessage);
            }

            // Store in chat history
            if (!options.SkipHistory && conversationId is not null)
            {
                await _chatHistory.StoreExchangeAsync(
                    conversationId.Value, userGuid,
                    exchange.UserMessage, exchange.AssistantMessage,
                    exchange.UserTimestamp, exchange.AssistantTimestamp,
                    ct);
            }

            // Build memories
            if (!options.SkipMemory)
            {
                try
                {
                    await _memory.AddAsync(exchange.UserMessage, exchange.AssistantMessage, userId, ct);
                    memorySuccessCount++;
                }
                catch (Exception ex)
                {
                    memoryFailCount++;
                    _logger.LogDebug(ex, "Memory add failed for exchange {Index} in {SourceId}", i, conv.SourceId);
                }

                // Track sentiment
                _memory.TrackSentiment(userId, emotionalChannelId, exchange.UserMessage);
            }

            // Rate limit
            if (options.DelayMs > 0)
                await Task.Delay(options.DelayMs, ct);
        }

        sw.Stop();

        // Update last activity timestamp
        if (!options.SkipHistory && conversationId is not null)
        {
            await _chatHistory.UpdateConversationActivityAsync(
                conversationId.Value,
                conv.Exchanges[^1].AssistantTimestamp,
                ct);
        }

        // Finalize emotional session
        if (!options.SkipMemory)
        {
            await _memory.FinalizeSessionAsync(userId, emotionalChannelId, ct);
        }

        // Summary for this conversation
        _logger.LogInformation("  Done in {Elapsed:F1}s — {Exchanges} exchanges, memory: {Ok} ok / {Fail} failed",
            sw.Elapsed.TotalSeconds, conv.Exchanges.Count, memorySuccessCount, memoryFailCount);
    }

    private List<BackfillConversation> ParseAll(string chatsDir, string? sourceFilter)
    {
        var conversations = new List<BackfillConversation>();

        // ChatGPT exports
        if (sourceFilter is null or "chatgpt")
        {
            var chatgptDir = Path.Combine(chatsDir, "chatgpt-export-json-mara");
            if (Directory.Exists(chatgptDir))
            {
                var parsed = ChatGptParser.ParseDirectory(chatgptDir);
                _logger.LogInformation("Parsed {Count} ChatGPT conversations from {Dir}", parsed.Count, chatgptDir);
                conversations.AddRange(parsed);
            }
            else
            {
                _logger.LogDebug("ChatGPT export dir not found: {Dir}", chatgptDir);
            }
        }

        // Discord DM
        if (sourceFilter is null or "discord-dm")
        {
            var dmFiles = Directory.GetFiles(chatsDir, "Direct Messages*.json");
            _logger.LogInformation("Found {Count} Discord DM export files", dmFiles.Length);
            foreach (var file in dmFiles)
            {
                var parsed = DiscordParser.ParseDmFile(file);
                _logger.LogInformation("  {File}: {Count} conversations", Path.GetFileName(file), parsed.Count);
                conversations.AddRange(parsed);
            }
        }

        // Discord Server
        if (sourceFilter is null or "discord-server")
        {
            var serverFiles = Directory.GetFiles(chatsDir, "JORSHTOPIA*.json");
            _logger.LogInformation("Found {Count} Discord Server export files", serverFiles.Length);
            foreach (var file in serverFiles)
            {
                var parsed = DiscordParser.ParseServerFile(file);
                _logger.LogInformation("  {File}: {Count} conversations", Path.GetFileName(file), parsed.Count);
                conversations.AddRange(parsed);
            }
        }

        return conversations;
    }
}

public sealed class BackfillOptions
{
    public required string ChatsDir { get; init; }
    public string? UserId { get; init; }
    public int DelayMs { get; init; } = 100;
    public int Concurrency { get; init; } = 3;
    public bool DryRun { get; init; }
    public string? Source { get; init; }
    public bool SkipMemory { get; init; }
    public bool SkipHistory { get; init; }
    public bool ResetCheckpoint { get; init; }
}
