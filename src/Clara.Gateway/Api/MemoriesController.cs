using Clara.Core.Data;
using Clara.Core.Memory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clara.Gateway.Api;

[ApiController]
[Route("api/v1/memories")]
public class MemoriesController : ControllerBase
{
    private readonly ClaraDbContext _db;
    private readonly IMemoryStore _memoryStore;

    public MemoriesController(ClaraDbContext db, IMemoryStore memoryStore)
    {
        _db = db;
        _memoryStore = memoryStore;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "userId is required" });

        var query = _db.Memories.Where(m => m.UserId == userId);
        var total = await query.CountAsync(ct);
        var memories = await query
            .OrderByDescending(m => m.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id,
                user_id = m.UserId,
                m.Content,
                m.Category,
                m.Score,
                access_count = m.AccessCount,
                created_at = m.CreatedAt,
                updated_at = m.UpdatedAt,
                last_accessed_at = m.LastAccessedAt,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            memories,
            total,
            page,
            page_size = pageSize,
        });
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] MemorySearchRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(request.UserId) || string.IsNullOrEmpty(request.Query))
            return BadRequest(new { error = "userId and query are required" });

        var results = await _memoryStore.SearchAsync(request.UserId, request.Query, request.Limit, ct);

        return Ok(new
        {
            results = results.Select(r => new
            {
                r.Entry.Id,
                r.Entry.Content,
                r.Entry.Category,
                r.Entry.Score,
                r.Relevance,
            }),
            query = request.Query,
        });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats([FromQuery] string? userId = null, CancellationToken ct = default)
    {
        var query = _db.Memories.AsQueryable();
        if (!string.IsNullOrEmpty(userId))
            query = query.Where(m => m.UserId == userId);

        var total = await query.CountAsync(ct);
        var avgScore = total > 0 ? await query.AverageAsync(m => m.Score, ct) : 0;
        var categories = await query
            .GroupBy(m => m.Category)
            .Select(g => new { category = g.Key, count = g.Count() })
            .ToListAsync(ct);

        return Ok(new
        {
            total,
            average_score = avgScore,
            categories,
        });
    }
}

public record MemorySearchRequest(string UserId, string Query, int Limit = 10);
