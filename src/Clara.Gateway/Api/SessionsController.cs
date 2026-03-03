using Clara.Core.Data;
using Clara.Core.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clara.Gateway.Api;

[ApiController]
[Route("api/v1/sessions")]
public class SessionsController : ControllerBase
{
    private readonly ClaraDbContext _db;

    public SessionsController(ClaraDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var query = _db.Sessions.AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(s => s.Status == status);

        var total = await query.CountAsync(ct);
        var sessions = await query
            .OrderByDescending(s => s.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                session_key = s.SessionKey,
                s.Title,
                s.Status,
                created_at = s.CreatedAt,
                updated_at = s.UpdatedAt,
                message_count = s.Messages.Count,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            sessions,
            total,
            page,
            page_size = pageSize,
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .Include(s => s.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (session is null)
            return NotFound(new { error = "Session not found" });

        return Ok(new
        {
            session.Id,
            session_key = session.SessionKey,
            session.Title,
            session.Status,
            session.Summary,
            created_at = session.CreatedAt,
            updated_at = session.UpdatedAt,
            ended_at = session.EndedAt,
            messages = session.Messages.Select(m => new
            {
                m.Id,
                m.Role,
                m.Content,
                created_at = m.CreatedAt,
            }),
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSessionRequest request, CancellationToken ct = default)
    {
        var session = await _db.Sessions.FindAsync([id], ct);
        if (session is null)
            return NotFound(new { error = "Session not found" });

        if (request.Title is not null)
            session.Title = request.Title;

        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { session.Id, session.Title, status = "updated" });
    }

    [HttpPatch("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct = default)
    {
        var session = await _db.Sessions.FindAsync([id], ct);
        if (session is null)
            return NotFound(new { error = "Session not found" });

        session.Status = "archived";
        session.EndedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { session.Id, status = "archived" });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (session is null)
            return NotFound(new { error = "Session not found" });

        _db.Messages.RemoveRange(session.Messages);
        _db.Sessions.Remove(session);
        await _db.SaveChangesAsync(ct);

        return Ok(new { status = "deleted" });
    }
}

public record UpdateSessionRequest(string? Title);
