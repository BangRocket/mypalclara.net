using MyPalClara.Api.Extensions;
using MyPalClara.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
public class AdminController : ControllerBase
{
    private readonly ClaraDbContext _db;

    public AdminController(ClaraDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// GET /api/v1/admin/users?status=pending&amp;offset=0&amp;limit=50
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(
        [FromQuery] string? status = null,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50)
    {
        HttpContext.RequireAdminUser();

        var query = _db.CanonicalUsers.AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(u => u.Status == status);

        var total = await query.CountAsync();

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(u => new
            {
                u.Id,
                DisplayName = u.DisplayName,
                Email = u.PrimaryEmail,
                AvatarUrl = u.AvatarUrl,
                u.Status,
                IsAdmin = u.IsAdmin,
                CreatedAt = u.CreatedAt,
                Platforms = u.PlatformLinks.Select(pl => new
                {
                    pl.Platform,
                    DisplayName = pl.DisplayName
                }).ToList()
            })
            .ToListAsync();

        return Ok(new { users, total, offset, limit });
    }

    /// <summary>
    /// GET /api/v1/admin/users/pending/count
    /// </summary>
    [HttpGet("users/pending/count")]
    public async Task<IActionResult> PendingCount()
    {
        HttpContext.RequireAdminUser();

        var count = await _db.CanonicalUsers
            .CountAsync(u => u.Status == "pending");

        return Ok(new { count });
    }

    /// <summary>
    /// POST /api/v1/admin/users/{id}/approve
    /// </summary>
    [HttpPost("users/{id}/approve")]
    public async Task<IActionResult> ApproveUser(string id)
    {
        HttpContext.RequireAdminUser();

        var user = await _db.CanonicalUsers.FindAsync(id);
        if (user == null)
            return NotFound(new { error = "User not found" });

        user.Status = "active";
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, user_id = user.Id, status = user.Status });
    }

    /// <summary>
    /// POST /api/v1/admin/users/{id}/suspend
    /// </summary>
    [HttpPost("users/{id}/suspend")]
    public async Task<IActionResult> SuspendUser(string id)
    {
        var admin = HttpContext.RequireAdminUser();

        if (admin.Id == id)
            return BadRequest(new { error = "Cannot suspend yourself" });

        var user = await _db.CanonicalUsers.FindAsync(id);
        if (user == null)
            return NotFound(new { error = "User not found" });

        user.Status = "suspended";
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, user_id = user.Id, status = user.Status });
    }
}
