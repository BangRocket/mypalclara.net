using MyPalClara.Api.Extensions;
using MyPalClara.Api.Services;
using MyPalClara.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Api.Controllers;

[ApiController]
[Route("api/v1/sessions")]
public class SessionsController : ControllerBase
{
    private readonly ClaraDbContext _db;
    private readonly UserIdentityService _identity;

    public SessionsController(ClaraDbContext db, UserIdentityService identity)
    {
        _db = db;
        _identity = identity;
    }

    /// <summary>
    /// GET /api/v1/sessions?offset=0&amp;limit=20&amp;archived=false
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20,
        [FromQuery] bool archived = false)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var archivedStr = archived ? "true" : "false";

        var query = _db.Sessions
            .Where(s => userIds.Contains(s.UserId) && s.Archived == archivedStr);

        var total = await query.CountAsync();

        var sessions = await query
            .OrderByDescending(s => s.LastActivityAt)
            .Skip(offset)
            .Take(limit)
            .Select(s => new
            {
                s.Id,
                s.Title,
                UserId = s.UserId,
                ContextId = s.ContextId,
                StartedAt = s.StartedAt,
                LastActivityAt = s.LastActivityAt,
                SessionSummary = s.SessionSummary,
                Archived = s.Archived == "true"
            })
            .ToListAsync();

        return Ok(new { sessions, total, offset, limit });
    }

    /// <summary>
    /// GET /api/v1/sessions/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var session = await _db.Sessions
            .Where(s => s.Id == id && userIds.Contains(s.UserId))
            .Select(s => new
            {
                s.Id,
                s.Title,
                UserId = s.UserId,
                ContextId = s.ContextId,
                StartedAt = s.StartedAt,
                LastActivityAt = s.LastActivityAt,
                SessionSummary = s.SessionSummary,
                ContextSnapshot = s.ContextSnapshot,
                Messages = s.Messages
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => new
                    {
                        m.Id,
                        m.Role,
                        m.Content,
                        CreatedAt = m.CreatedAt
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        if (session == null)
            return NotFound(new { error = "Session not found" });

        return Ok(session);
    }

    /// <summary>
    /// PUT /api/v1/sessions/{id} — rename session
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateSessionRequest request)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == id && userIds.Contains(s.UserId));

        if (session == null)
            return NotFound(new { error = "Session not found" });

        session.Title = request.Title;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, id = session.Id, title = session.Title });
    }

    /// <summary>
    /// PATCH /api/v1/sessions/{id}/archive
    /// </summary>
    [HttpPatch("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == id && userIds.Contains(s.UserId));

        if (session == null)
            return NotFound(new { error = "Session not found" });

        session.Archived = "true";
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, id = session.Id });
    }

    /// <summary>
    /// PATCH /api/v1/sessions/{id}/unarchive
    /// </summary>
    [HttpPatch("{id}/unarchive")]
    public async Task<IActionResult> Unarchive(string id)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == id && userIds.Contains(s.UserId));

        if (session == null)
            return NotFound(new { error = "Session not found" });

        session.Archived = "false";
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, id = session.Id });
    }

    /// <summary>
    /// DELETE /api/v1/sessions/{id} — cascade delete messages
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var session = await _db.Sessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == id && userIds.Contains(s.UserId));

        if (session == null)
            return NotFound(new { error = "Session not found" });

        _db.Messages.RemoveRange(session.Messages);
        _db.Sessions.Remove(session);
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, id });
    }
}

public class UpdateSessionRequest
{
    public string Title { get; set; } = null!;
}
