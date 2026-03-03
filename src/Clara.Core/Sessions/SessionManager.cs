using Clara.Core.Data;
using Clara.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Sessions;

public class SessionManager : ISessionManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(IServiceScopeFactory scopeFactory, ILogger<SessionManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<Session> GetOrCreateAsync(string sessionKey, string? userId = null, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var entity = await db.Sessions
            .FirstOrDefaultAsync(s => s.SessionKey == sessionKey && s.Status == "active", ct);

        if (entity is not null)
        {
            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return ToSession(entity);
        }

        // Create new session
        var now = DateTime.UtcNow;
        var newEntity = new SessionEntity
        {
            Id = Guid.NewGuid(),
            SessionKey = sessionKey,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Sessions.Add(newEntity);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Created new session: {SessionKey}", sessionKey);

        return ToSession(newEntity);
    }

    public async Task<Session?> GetAsync(string sessionKey, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var entity = await db.Sessions
            .FirstOrDefaultAsync(s => s.SessionKey == sessionKey, ct);

        return entity is not null ? ToSession(entity) : null;
    }

    public async Task UpdateAsync(Session session, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var entity = await db.Sessions
            .FirstOrDefaultAsync(s => s.Id == session.Id, ct);

        if (entity is null)
        {
            _logger.LogWarning("Session not found for update: {SessionId}", session.Id);
            return;
        }

        entity.Title = session.Title;
        entity.Status = session.Status;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task TimeoutAsync(string sessionKey, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var entity = await db.Sessions
            .FirstOrDefaultAsync(s => s.SessionKey == sessionKey && s.Status == "active", ct);

        if (entity is null) return;

        entity.Status = "timeout";
        entity.EndedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Session timed out: {SessionKey}", sessionKey);
    }

    private static Session ToSession(SessionEntity entity)
    {
        return new Session
        {
            Id = entity.Id,
            Key = SessionKey.Parse(entity.SessionKey),
            UserId = entity.UserId?.ToString(),
            Title = entity.Title,
            Status = entity.Status,
            CreatedAt = entity.CreatedAt,
            LastActivityAt = entity.UpdatedAt,
        };
    }
}
