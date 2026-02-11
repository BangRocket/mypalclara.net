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
    /// When <paramref name="linkTo"/> is set, links to the same CanonicalUser as that user
    /// instead of creating a new one — enabling cross-platform context sharing.
    /// </summary>
    public async Task EnsurePlatformLinkAsync(string prefixedUserId, string? displayName = null, string? linkTo = null)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var existing = await db.PlatformLinks
                .FirstOrDefaultAsync(l => l.PrefixedUserId == prefixedUserId);

            // If link exists and linkTo is set, re-link if pointing to wrong canonical user
            if (existing is not null)
            {
                if (!string.IsNullOrEmpty(linkTo))
                {
                    var targetLink = await db.PlatformLinks
                        .FirstOrDefaultAsync(l => l.PrefixedUserId == linkTo);
                    if (targetLink is not null && existing.CanonicalUserId != targetLink.CanonicalUserId)
                    {
                        existing.CanonicalUserId = targetLink.CanonicalUserId;
                        existing.LinkedVia = "config";
                        await db.SaveChangesAsync();
                        _logger.LogInformation("Re-linked {UserId} to CanonicalUser via {LinkTo}", prefixedUserId, linkTo);
                    }
                }
                return;
            }

            var parts = prefixedUserId.Split('-', 2);
            var platform = parts.Length > 1 ? parts[0] : "cli";
            var platformUserId = parts.Length > 1 ? parts[1] : prefixedUserId;
            var name = displayName ?? prefixedUserId;

            // If linkTo is specified, find that user's CanonicalUser and reuse it
            string canonicalId;
            if (!string.IsNullOrEmpty(linkTo))
            {
                var targetLink = await db.PlatformLinks
                    .FirstOrDefaultAsync(l => l.PrefixedUserId == linkTo);
                if (targetLink is not null)
                {
                    canonicalId = targetLink.CanonicalUserId;
                    _logger.LogInformation("Linking {UserId} to existing CanonicalUser via {LinkTo}", prefixedUserId, linkTo);
                }
                else
                {
                    _logger.LogWarning("link_to target {LinkTo} not found, creating new CanonicalUser", linkTo);
                    var canonical = new CanonicalUserEntity { DisplayName = name };
                    db.CanonicalUsers.Add(canonical);
                    await db.SaveChangesAsync();
                    canonicalId = canonical.Id;
                }
            }
            else
            {
                var canonical = new CanonicalUserEntity { DisplayName = name };
                db.CanonicalUsers.Add(canonical);
                await db.SaveChangesAsync();
                canonicalId = canonical.Id;
            }

            db.PlatformLinks.Add(new PlatformLinkEntity
            {
                CanonicalUserId = canonicalId,
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
