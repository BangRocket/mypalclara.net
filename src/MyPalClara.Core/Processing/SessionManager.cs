using MyPalClara.Data;
using MyPalClara.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Core.Processing;

/// <summary>
/// Handles DB session lifecycle: creation, lookup, message storage.
/// </summary>
public class SessionManager
{
    private const int SessionTimeoutMinutes = 30;

    private readonly ILogger<SessionManager> _logger;

    public SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Find or create a session for the given user and channel.
    /// Returns (sessionId, projectId).
    /// </summary>
    public async Task<(string SessionId, string ProjectId)> GetOrCreateSessionAsync(
        ClaraDbContext db, string userId, string channelId, bool isDm)
    {
        var contextId = isDm ? $"dm-{userId}" : $"channel-{channelId}";

        // Find or create project
        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.OwnerId == userId);

        if (project is null)
        {
            project = new Project
            {
                Id = Guid.NewGuid().ToString(),
                OwnerId = userId,
                Name = "Default Project",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            _logger.LogInformation("Created project {ProjectId} for user {UserId}", project.Id, userId);
        }

        // Find existing active session
        var cutoff = DateTime.UtcNow.AddMinutes(-SessionTimeoutMinutes);
        var session = await db.Sessions
            .Where(s => s.UserId == userId
                        && s.ContextId == contextId
                        && s.ProjectId == project.Id
                        && s.Archived == "false")
            .OrderByDescending(s => s.LastActivityAt)
            .FirstOrDefaultAsync();

        if (session is not null && session.LastActivityAt >= cutoff)
        {
            // Active session: update activity timestamp
            session.LastActivityAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return (session.Id, project.Id);
        }

        // Session timed out or not found: create new one
        var previousSessionId = session?.Id;

        // If there was a timed-out session, get its summary for continuity
        string? previousSummary = session?.SessionSummary;

        var newSession = new Session
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = project.Id,
            UserId = userId,
            ContextId = contextId,
            Archived = "false",
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            PreviousSessionId = previousSessionId
        };

        // Archive the old session if it existed
        if (session is not null)
        {
            session.Archived = "true";
            _logger.LogDebug(
                "Archived timed-out session {OldSessionId}, creating new {NewSessionId}",
                session.Id, newSession.Id);
        }

        db.Sessions.Add(newSession);
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Created session {SessionId} for user {UserId} context {ContextId}",
            newSession.Id, userId, contextId);

        return (newSession.Id, project.Id);
    }

    /// <summary>
    /// Store a message in the database.
    /// </summary>
    public async Task StoreMessageAsync(
        ClaraDbContext db, string sessionId, string userId, string role, string content)
    {
        var message = new Message
        {
            SessionId = sessionId,
            UserId = userId,
            Role = role,
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Get recent messages from the database for a session.
    /// </summary>
    public async Task<List<DbMessageDto>> GetRecentMessagesAsync(
        ClaraDbContext db, string sessionId, int limit = 30)
    {
        return await db.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .OrderBy(m => m.CreatedAt) // Return in chronological order
            .Select(m => new DbMessageDto(m.Role, m.Content, m.CreatedAt))
            .ToListAsync();
    }

    /// <summary>
    /// Update the last activity timestamp for a session.
    /// </summary>
    public async Task UpdateSessionActivityAsync(ClaraDbContext db, string sessionId)
    {
        var session = await db.Sessions.FindAsync(sessionId);
        if (session is not null)
        {
            session.LastActivityAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}
