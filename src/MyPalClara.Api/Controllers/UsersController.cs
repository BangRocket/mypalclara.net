using MyPalClara.Api.Extensions;
using MyPalClara.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly ClaraDbContext _db;

    public UsersController(ClaraDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// GET /api/v1/users/me — profile with platform links
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var user = HttpContext.RequireApprovedUser();

        var links = await _db.PlatformLinks
            .Where(pl => pl.CanonicalUserId == user.Id)
            .Select(pl => new
            {
                pl.Id,
                pl.Platform,
                PlatformUserId = pl.PlatformUserId,
                PrefixedUserId = pl.PrefixedUserId,
                DisplayName = pl.DisplayName,
                LinkedAt = pl.LinkedAt,
                LinkedVia = pl.LinkedVia
            })
            .ToListAsync();

        return Ok(new
        {
            user.Id,
            DisplayName = user.DisplayName,
            Email = user.PrimaryEmail,
            AvatarUrl = user.AvatarUrl,
            CreatedAt = user.CreatedAt,
            Links = links
        });
    }

    /// <summary>
    /// PUT /api/v1/users/me — update display_name, avatar_url
    /// </summary>
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateUserRequest request)
    {
        var user = HttpContext.RequireApprovedUser();

        // Re-fetch to get a tracked entity
        var dbUser = await _db.CanonicalUsers.FindAsync(user.Id);
        if (dbUser == null)
            return NotFound(new { error = "User not found" });

        if (request.DisplayName != null)
            dbUser.DisplayName = request.DisplayName;

        if (request.AvatarUrl != null)
            dbUser.AvatarUrl = request.AvatarUrl;

        dbUser.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    /// <summary>
    /// GET /api/v1/users/me/links — platform links
    /// </summary>
    [HttpGet("me/links")]
    public async Task<IActionResult> GetLinks()
    {
        var user = HttpContext.RequireApprovedUser();

        var links = await _db.PlatformLinks
            .Where(pl => pl.CanonicalUserId == user.Id)
            .Select(pl => new
            {
                pl.Id,
                pl.Platform,
                PlatformUserId = pl.PlatformUserId,
                PrefixedUserId = pl.PrefixedUserId,
                DisplayName = pl.DisplayName,
                LinkedAt = pl.LinkedAt,
                LinkedVia = pl.LinkedVia
            })
            .ToListAsync();

        return Ok(new { links });
    }
}

public class UpdateUserRequest
{
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
}
