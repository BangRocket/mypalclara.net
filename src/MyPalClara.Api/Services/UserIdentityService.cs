using MyPalClara.Data;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Api.Services;

/// <summary>
/// Resolves cross-platform user identities via the PlatformLink table.
/// Matches the Python resolve_all_user_ids_for_canonical() function.
/// </summary>
public class UserIdentityService
{
    private readonly ClaraDbContext _db;

    public UserIdentityService(ClaraDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns all prefixed_user_ids linked to a canonical user.
    /// Used to query sessions/intentions/etc. that may be stored under any platform identity.
    /// </summary>
    public async Task<List<string>> ResolveAllUserIds(string canonicalUserId)
    {
        var prefixedIds = await _db.PlatformLinks
            .Where(pl => pl.CanonicalUserId == canonicalUserId)
            .Select(pl => pl.PrefixedUserId)
            .ToListAsync();

        return prefixedIds;
    }
}
