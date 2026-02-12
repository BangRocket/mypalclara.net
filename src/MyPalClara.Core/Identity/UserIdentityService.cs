using MyPalClara.Core.Data;
using MyPalClara.Core.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Core.Identity;

/// <summary>
/// Cross-platform user identity resolution.
/// Resolves a single prefixed_user_id to all linked platform IDs.
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
                .Where(l => l.UserId == link.UserId)
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
    /// Resolve a prefixed user ID to its internal User Guid.
    /// Creates the User + PlatformLink if none exists.
    /// </summary>
    public async Task<Guid?> ResolveUserGuidAsync(string prefixedUserId, string? displayName = null)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var link = await db.PlatformLinks
                .FirstOrDefaultAsync(l => l.PrefixedUserId == prefixedUserId);

            if (link is not null)
                return link.UserId;

            // Auto-create
            var parts = prefixedUserId.Split('-', 2);
            var platform = parts.Length > 1 ? parts[0] : "cli";
            var platformUserId = parts.Length > 1 ? parts[1] : prefixedUserId;
            var name = displayName ?? prefixedUserId;

            var user = new UserEntity { DisplayName = name };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.PlatformLinks.Add(new PlatformLinkEntity
            {
                UserId = user.Id,
                Platform = platform,
                PlatformUserId = platformUserId,
                PrefixedUserId = prefixedUserId,
                DisplayName = name,
                LinkedVia = "auto",
            });
            await db.SaveChangesAsync();

            _logger.LogInformation("Auto-created User + PlatformLink for {UserId}", prefixedUserId);
            return user.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ResolveUserGuid failed for {UserId}", prefixedUserId);
            return null;
        }
    }

    /// <summary>
    /// Resolve all internal User Guids linked to a prefixed user ID.
    /// Returns a single-element list in the common case.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ResolveAllUserGuidsAsync(string prefixedUserId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var link = await db.PlatformLinks
                .FirstOrDefaultAsync(l => l.PrefixedUserId == prefixedUserId);

            if (link is null)
                return [];

            // All users linked to the same canonical user (via same User row)
            return [link.UserId];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ResolveAllUserGuids failed for {UserId}", prefixedUserId);
            return [];
        }
    }

    /// <summary>
    /// Auto-create User + PlatformLink if none exists (idempotent).
    /// When linkTo is set, links to the same User as that user.
    /// </summary>
    public async Task EnsurePlatformLinkAsync(string prefixedUserId, string? displayName = null, string? linkTo = null)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var existing = await db.PlatformLinks
                .FirstOrDefaultAsync(l => l.PrefixedUserId == prefixedUserId);

            if (existing is not null)
            {
                if (!string.IsNullOrEmpty(linkTo))
                {
                    var targetLink = await db.PlatformLinks
                        .FirstOrDefaultAsync(l => l.PrefixedUserId == linkTo);
                    if (targetLink is not null && existing.UserId != targetLink.UserId)
                    {
                        existing.UserId = targetLink.UserId;
                        existing.LinkedVia = "config";
                        await db.SaveChangesAsync();
                        _logger.LogInformation("Re-linked {UserId} to User via {LinkTo}", prefixedUserId, linkTo);
                    }
                }
                return;
            }

            var parts = prefixedUserId.Split('-', 2);
            var platform = parts.Length > 1 ? parts[0] : "cli";
            var platformUserId = parts.Length > 1 ? parts[1] : prefixedUserId;
            var name = displayName ?? prefixedUserId;

            Guid userId;
            if (!string.IsNullOrEmpty(linkTo))
            {
                var targetLink = await db.PlatformLinks
                    .FirstOrDefaultAsync(l => l.PrefixedUserId == linkTo);
                if (targetLink is not null)
                {
                    userId = targetLink.UserId;
                    _logger.LogInformation("Linking {PrefixedUserId} to existing User via {LinkTo}", prefixedUserId, linkTo);
                }
                else
                {
                    _logger.LogWarning("link_to target {LinkTo} not found, creating new User", linkTo);
                    var user = new UserEntity { DisplayName = name };
                    db.Users.Add(user);
                    await db.SaveChangesAsync();
                    userId = user.Id;
                }
            }
            else
            {
                var user = new UserEntity { DisplayName = name };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                userId = user.Id;
            }

            db.PlatformLinks.Add(new PlatformLinkEntity
            {
                UserId = userId,
                Platform = platform,
                PlatformUserId = platformUserId,
                PrefixedUserId = prefixedUserId,
                DisplayName = name,
                LinkedVia = linkTo is not null ? "config" : "auto",
            });
            await db.SaveChangesAsync();

            _logger.LogInformation("Auto-created PlatformLink for {UserId}", prefixedUserId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnsurePlatformLink failed for {UserId}", prefixedUserId);
        }
    }
}
