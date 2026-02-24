using MyPalClara.Data.Entities;

namespace MyPalClara.Api.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// Returns the authenticated CanonicalUser from HttpContext.Items, or null if not set.
    /// </summary>
    public static CanonicalUser? GetCurrentUser(this HttpContext ctx)
    {
        return ctx.Items.TryGetValue("CurrentUser", out var user) ? user as CanonicalUser : null;
    }

    /// <summary>
    /// Returns the authenticated CanonicalUser if their status is "active".
    /// Throws InvalidOperationException if no user or user is not active.
    /// </summary>
    public static CanonicalUser RequireApprovedUser(this HttpContext ctx)
    {
        var user = ctx.GetCurrentUser()
            ?? throw new InvalidOperationException("No authenticated user");

        if (user.Status != "active")
            throw new InvalidOperationException($"User status is '{user.Status}', expected 'active'");

        return user;
    }

    /// <summary>
    /// Returns the authenticated CanonicalUser if they are an admin.
    /// Throws InvalidOperationException if no user or user is not admin.
    /// </summary>
    public static CanonicalUser RequireAdminUser(this HttpContext ctx)
    {
        var user = ctx.GetCurrentUser()
            ?? throw new InvalidOperationException("No authenticated user");

        if (!user.IsAdmin)
            throw new InvalidOperationException("Admin access required");

        return user;
    }
}
