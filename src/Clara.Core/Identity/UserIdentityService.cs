using Clara.Core.Data;
using Clara.Core.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Identity;

/// <summary>
/// Cross-platform user identity resolution.
/// Port of db/user_identity.py — resolves a single prefixed_user_id to all linked platform IDs.
/// </summary>
public sealed class UserIdentityService
{
    private readonly IDbContextFactory<ClaraDbContext> _dbFactory;
    private readonly ILogger<UserIdentityService> _logger;

    public UserIdentityService(IDbContextFactory<ClaraDbContext> dbFactory, ILogger<UserIdentityService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Resolve a prefixed user ID (e.g. "discord-123") to ALL linked user IDs across platforms.
    /// Falls back to [prefixedUserId] if no link exists or DB is unavailable.
    /// </summary>
    public async Task<IReadOnlyList<string>> ResolveAllUserIdsAsync(string prefixedUserId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var link = await db.PlatformLinks
                .FirstOrDefaultAsync(l => l.PrefixedUserId == prefixedUserId);

            if (link is null)
                return [prefixedUserId];

            var allIds = await db.PlatformLinks
                .Where(l => l.CanonicalUserId == link.CanonicalUserId)
                .Select(l => l.PrefixedUserId)
                .ToListAsync();

            return allIds.Count > 0 ? allIds : [prefixedUserId];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "resolve_all_user_ids failed for {UserId}, falling back to single", prefixedUserId);
            return [prefixedUserId];
        }
    }

    /// <summary>
    /// Auto-create CanonicalUser + PlatformLink if none exists (idempotent).
    /// Called on first message from a new user.
    /// </summary>
    public async Task EnsurePlatformLinkAsync(string prefixedUserId, string? displayName = null)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var exists = await db.PlatformLinks
                .AnyAsync(l => l.PrefixedUserId == prefixedUserId);
            if (exists) return;

            var parts = prefixedUserId.Split('-', 2);
            var platform = parts.Length > 1 ? parts[0] : "cli";
            var platformUserId = parts.Length > 1 ? parts[1] : prefixedUserId;
            var name = displayName ?? prefixedUserId;

            var canonical = new CanonicalUserEntity { DisplayName = name };
            db.CanonicalUsers.Add(canonical);
            await db.SaveChangesAsync();

            db.PlatformLinks.Add(new PlatformLinkEntity
            {
                CanonicalUserId = canonical.Id,
                Platform = platform,
                PlatformUserId = platformUserId,
                PrefixedUserId = prefixedUserId,
                DisplayName = name,
                LinkedVia = "auto",
            });
            await db.SaveChangesAsync();

            _logger.LogInformation("Auto-created CanonicalUser + PlatformLink for {UserId}", prefixedUserId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnsurePlatformLink failed for {UserId}", prefixedUserId);
        }
    }
}
