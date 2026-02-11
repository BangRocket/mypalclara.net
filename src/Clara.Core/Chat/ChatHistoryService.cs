using Clara.Core.Data;
using Clara.Core.Data.Models;
using Clara.Core.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Chat;

/// <summary>
/// PostgreSQL-backed conversation persistence.
/// Shares projects/sessions/messages tables with Python Clara so
/// CLI sessions are visible in web UI and vice versa.
/// </summary>
public sealed class ChatHistoryService
{
    private readonly IDbContextFactory<ClaraDbContext> _dbFactory;
    private readonly ILogger<ChatHistoryService> _logger;

    private string? _currentSessionId;

    public string? CurrentSessionId => _currentSessionId;

    public ChatHistoryService(IDbContextFactory<ClaraDbContext> dbFactory, ILogger<ChatHistoryService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>Build CLI context ID matching Python gateway path for CLI DMs.</summary>
    public static string BuildCliContextId(string userId) => $"dm-{userId}";

    /// <summary>
    /// Get or create an active session for the given user/context.
    /// Mirrors Python's _get_or_create_db_session logic.
    /// </summary>
    public async Task<string?> GetOrCreateSessionAsync(string userId, string contextId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Get or create default project for this user
            var project = await db.Projects
                .FirstOrDefaultAsync(p => p.OwnerId == userId, ct);

            if (project is null)
            {
                project = new ProjectEntity
                {
                    OwnerId = userId,
                    Name = "Default",
                };
                db.Projects.Add(project);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Created default project {ProjectId} for {UserId}", project.Id, userId);
            }

            // Find active (non-archived) session
            var session = await db.Sessions
                .Where(s => s.UserId == userId && s.ContextId == contextId && s.ProjectId == project.Id && s.Archived != "true")
                .OrderByDescending(s => s.LastActivityAt)
                .FirstOrDefaultAsync(ct);

            if (session is not null)
            {
                _currentSessionId = session.Id;
                _logger.LogDebug("Resumed session {SessionId}", session.Id);
                return session.Id;
            }

            // Find most recent prior session for chaining
            var previousSession = await db.Sessions
                .Where(s => s.UserId == userId && s.ContextId == contextId && s.ProjectId == project.Id)
                .OrderByDescending(s => s.LastActivityAt)
                .FirstOrDefaultAsync(ct);

            session = new SessionEntity
            {
                UserId = userId,
                ProjectId = project.Id,
                ContextId = contextId,
                PreviousSessionId = previousSession?.Id,
            };
            db.Sessions.Add(session);
            await db.SaveChangesAsync(ct);

            _currentSessionId = session.Id;
            _logger.LogInformation("Created new session {SessionId} for {UserId}", session.Id, userId);
            return session.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetOrCreateSession failed for {UserId}", userId);
            return null;
        }
    }

    /// <summary>Persist a user/assistant message pair.</summary>
    public async Task StoreExchangeAsync(string sessionId, string userId, string userMsg, string assistantMsg, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;

            db.Messages.Add(new MessageEntity
            {
                SessionId = sessionId,
                UserId = userId,
                Role = "user",
                Content = userMsg,
                CreatedAt = now,
            });

            db.Messages.Add(new MessageEntity
            {
                SessionId = sessionId,
                UserId = userId,
                Role = "assistant",
                Content = assistantMsg,
                CreatedAt = now.AddMilliseconds(1),
            });

            // Touch session activity
            var session = await db.Sessions.FindAsync([sessionId], ct);
            if (session is not null)
                session.LastActivityAt = now;

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StoreExchange failed for session {SessionId}", sessionId);
        }
    }

    /// <summary>Load recent messages from a session, returned as ChatMessage list.</summary>
    public async Task<List<ChatMessage>> LoadRecentMessagesAsync(string sessionId, int count = 15, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var entities = await db.Messages
                .Where(m => m.SessionId == sessionId)
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
            _logger.LogWarning(ex, "LoadRecentMessages failed for session {SessionId}", sessionId);
            return [];
        }
    }

    /// <summary>List recent sessions across all linked user IDs (cross-platform visibility).</summary>
    public async Task<List<SessionEntity>> GetUserSessionsAsync(IReadOnlyList<string> userIds, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.Sessions
                .Where(s => userIds.Contains(s.UserId))
                .OrderByDescending(s => s.LastActivityAt)
                .Take(limit)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetUserSessions failed");
            return [];
        }
    }

    /// <summary>Touch session last_activity_at timestamp.</summary>
    public async Task UpdateSessionActivityAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var session = await db.Sessions.FindAsync([sessionId], ct);
            if (session is not null)
            {
                session.LastActivityAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UpdateSessionActivity failed for session {SessionId}", sessionId);
        }
    }
}
