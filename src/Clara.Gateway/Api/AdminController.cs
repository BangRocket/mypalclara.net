using Clara.Core.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clara.Gateway.Api;

[ApiController]
[Route("api/v1/admin")]
public class AdminController : ControllerBase
{
    private readonly ClaraDbContext _db;

    public AdminController(ClaraDbContext db)
    {
        _db = db;
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var total = await _db.Users.CountAsync(ct);
        var users = await _db.Users
            .OrderByDescending(u => u.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                platform_id = u.PlatformId,
                u.Platform,
                display_name = u.DisplayName,
                u.Email,
                created_at = u.CreatedAt,
                updated_at = u.UpdatedAt,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            users,
            total,
            page,
            page_size = pageSize,
        });
    }
}
