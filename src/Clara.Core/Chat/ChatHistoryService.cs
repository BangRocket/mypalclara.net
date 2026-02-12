using Clara.Core.Data;
using Clara.Core.Data.Models;
using Clara.Core.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Chat;

/// <summary>
/// PostgreSQL-backed conversation persistence using the new adapter/channel/conversation schema.
/// Supports cross-adapter context retrieval for unified Clara experience.
/// </summary>
public sealed class ChatHistoryService
{
    private readonly IDbContextFactory<ClaraDbContext> _dbFactory;
    private readonly ILogger<ChatHistoryService> _logger;

    private Guid? _currentConversationId;

    public Guid? CurrentConversationId => _currentConversationId;

    public ChatHistoryService(IDbContextFactory<ClaraDbContext> dbFactory, ILogger<ChatHistoryService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get or create an active conversation for the given channel and user.
    /// </summary>
    public async Task<Guid?> GetOrCreateConversationAsync(Guid channelId, Guid userId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Find active (non-archived) conversation
            var conversation = await db.Conversations
                .Where(c => c.ChannelId == channelId && c.UserId == userId && !c.Archived)
                .OrderByDescending(c => c.LastActivityAt)
                .FirstOrDefaultAsync(ct);

            if (conversation is not null)
            {
                _currentConversationId = conversation.Id;
                _logger.LogDebug("Resumed conversation {ConversationId}", conversation.Id);
                return conversation.Id;
            }

            // Find most recent prior conversation for chaining
            var previous = await db.Conversations
                .Where(c => c.ChannelId == channelId && c.UserId == userId)
                .OrderByDescending(c => c.LastActivityAt)
                .FirstOrDefaultAsync(ct);

            conversation = new ConversationEntity
            {
                ChannelId = channelId,
                UserId = userId,
                PreviousConversationId = previous?.Id,
            };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(ct);

            _currentConversationId = conversation.Id;
            _logger.LogInformation("Created new conversation {ConversationId}", conversation.Id);
            return conversation.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetOrCreateConversation failed");
            return null;
        }
    }

    /// <summary>Persist a user/assistant message pair.</summary>
    public async Task StoreExchangeAsync(Guid conversationId, Guid? userId, string userMsg, string assistantMsg, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            db.Messages.Add(new MessageEntity
            {
                ConversationId = conversationId,
                UserId = userId,
                Role = "user",
                Content = userMsg,
                CreatedAt = now,
            });

            db.Messages.Add(new MessageEntity
            {
                ConversationId = conversationId,
                UserId = null, // assistant messages have no user
                Role = "assistant",
                Content = assistantMsg,
                CreatedAt = now.AddMilliseconds(1),
            });

            // Touch conversation activity
            var conversation = await db.Conversations.FindAsync([conversationId], ct);
            if (conversation is not null)
                conversation.LastActivityAt = now;

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StoreExchange failed for conversation {ConversationId}", conversationId);
        }
    }

    /// <summary>Load recent messages from a conversation, returned as ChatMessage list.</summary>
    public async Task<List<ChatMessage>> LoadRecentMessagesAsync(Guid conversationId, int count = 15, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var entities = await db.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(count)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(ct);

            return entities.Select<MessageEntity, ChatMessage>(e => e.Role switch
            {
                "assistant" => new AssistantMessage(e.Content),
                _ => new UserMessage(e.Content),
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LoadRecentMessages failed for conversation {ConversationId}", conversationId);
            return [];
        }
    }

    /// <summary>
    /// Get recent messages across ALL active conversations for the given user IDs,
    /// annotated with adapter type + channel name for cross-context awareness.
    /// </summary>
    public async Task<List<CrossContextMessage>> GetRecentCrossContextAsync(
        IReadOnlyList<Guid> userIds, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var messages = await db.Messages
                .Include(m => m.Conversation!)
                    .ThenInclude(c => c.Channel!)
                        .ThenInclude(ch => ch.Adapter)
                .Where(m => m.Conversation != null
                    && userIds.Contains(m.Conversation.UserId)
                    && m.Role == "user")
                .OrderByDescending(m => m.CreatedAt)
                .Take(limit)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new CrossContextMessage
                {
                    AdapterType = m.Conversation!.Channel!.Adapter!.Type,
                    ChannelName = m.Conversation.Channel.Name,
                    Role = m.Role,
                    Content = m.Content,
                    CreatedAt = m.CreatedAt,
                })
                .ToListAsync(ct);

            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetRecentCrossContext failed");
            return [];
        }
    }

    /// <summary>List recent conversations across all linked user IDs.</summary>
    public async Task<List<ConversationEntity>> GetUserConversationsAsync(IReadOnlyList<Guid> userIds, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.Conversations
                .Where(c => userIds.Contains(c.UserId))
                .OrderByDescending(c => c.LastActivityAt)
                .Take(limit)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetUserConversations failed");
            return [];
        }
    }

    /// <summary>Generic adapter/channel creation for any adapter type (backfill, external integrations).</summary>
    public async Task<(Guid AdapterId, Guid ChannelId)?> EnsureChannelAsync(
        string adapterType, string adapterName,
        string externalId, string channelName, string channelType,
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var adapter = await db.Adapters
                .FirstOrDefaultAsync(a => a.Type == adapterType, ct);

            if (adapter is null)
            {
                adapter = new AdapterEntity { Type = adapterType, Name = adapterName };
                db.Adapters.Add(adapter);
                await db.SaveChangesAsync(ct);
            }

            var channel = await db.Channels
                .FirstOrDefaultAsync(c => c.AdapterId == adapter.Id && c.ExternalId == externalId, ct);

            if (channel is null)
            {
                channel = new ChannelEntity
                {
                    AdapterId = adapter.Id,
                    ExternalId = externalId,
                    Name = channelName,
                    ChannelType = channelType,
                };
                db.Channels.Add(channel);
                await db.SaveChangesAsync(ct);
            }

            return (adapter.Id, channel.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnsureChannel failed for {AdapterType}/{ExternalId}", adapterType, externalId);
            return null;
        }
    }

    /// <summary>Persist a user/assistant message pair with explicit historical timestamps.</summary>
    public async Task StoreExchangeAsync(
        Guid conversationId, Guid? userId,
        string userMsg, string assistantMsg,
        DateTime userTimestamp, DateTime assistantTimestamp,
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            db.Messages.Add(new MessageEntity
            {
                ConversationId = conversationId,
                UserId = userId,
                Role = "user",
                Content = userMsg,
                CreatedAt = userTimestamp,
            });

            db.Messages.Add(new MessageEntity
            {
                ConversationId = conversationId,
                UserId = null,
                Role = "assistant",
                Content = assistantMsg,
                CreatedAt = assistantTimestamp,
            });

            var conversation = await db.Conversations.FindAsync([conversationId], ct);
            if (conversation is not null)
                conversation.LastActivityAt = assistantTimestamp;

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StoreExchange (timestamped) failed for conversation {ConversationId}", conversationId);
        }
    }

    /// <summary>Create a conversation with a historical StartedAt timestamp (for backfill).</summary>
    public async Task<Guid?> CreateBackfillConversationAsync(
        Guid channelId, Guid userId, DateTime startedAt,
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var conversation = new ConversationEntity
            {
                ChannelId = channelId,
                UserId = userId,
                StartedAt = startedAt,
                LastActivityAt = startedAt,
                Archived = true, // historical conversations are archived
            };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(ct);

            _logger.LogDebug("Created backfill conversation {ConversationId} at {StartedAt}", conversation.Id, startedAt);
            return conversation.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CreateBackfillConversation failed");
            return null;
        }
    }

    /// <summary>Update conversation LastActivityAt (for backfill finalization).</summary>
    public async Task UpdateConversationActivityAsync(Guid conversationId, DateTime lastActivity, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var conversation = await db.Conversations.FindAsync([conversationId], ct);
            if (conversation is not null)
            {
                conversation.LastActivityAt = lastActivity;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UpdateConversationActivity failed for {ConversationId}", conversationId);
        }
    }

    /// <summary>Get or create a CLI adapter and channel for the given user.</summary>
    public async Task<(Guid AdapterId, Guid ChannelId)?> EnsureCliChannelAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Get or create CLI adapter
            var adapter = await db.Adapters
                .FirstOrDefaultAsync(a => a.Type == "cli", ct);

            if (adapter is null)
            {
                adapter = new AdapterEntity { Type = "cli", Name = "CLI" };
                db.Adapters.Add(adapter);
                await db.SaveChangesAsync(ct);
            }

            // Get or create DM channel for this user
            var externalId = $"dm-{userId}";
            var channel = await db.Channels
                .FirstOrDefaultAsync(c => c.AdapterId == adapter.Id && c.ExternalId == externalId, ct);

            if (channel is null)
            {
                channel = new ChannelEntity
                {
                    AdapterId = adapter.Id,
                    ExternalId = externalId,
                    Name = "CLI DM",
                    ChannelType = "dm",
                };
                db.Channels.Add(channel);
                await db.SaveChangesAsync(ct);
            }

            return (adapter.Id, channel.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnsureCliChannel failed");
            return null;
        }
    }
}

/// <summary>A message annotated with its source adapter and channel for cross-context display.</summary>
public sealed class CrossContextMessage
{
    public required string AdapterType { get; init; }
    public required string ChannelName { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }

    public override string ToString()
    {
        var ago = DateTime.UtcNow - CreatedAt;
        var agoStr = ago.TotalMinutes < 1 ? "now" :
            ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes} min ago" :
            $"{(int)ago.TotalHours}h ago";
        return $"[{AdapterType} {ChannelName}, {agoStr}] {Content}";
    }
}
