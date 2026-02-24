using System.Text.Json;
using MyPalClara.Api.Extensions;
using MyPalClara.Api.Services;
using MyPalClara.Data;
using MyPalClara.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Api.Controllers;

[ApiController]
[Route("api/v1/intentions")]
public class IntentionsController : ControllerBase
{
    private readonly ClaraDbContext _db;
    private readonly UserIdentityService _identity;

    public IntentionsController(ClaraDbContext db, UserIdentityService identity)
    {
        _db = db;
        _identity = identity;
    }

    /// <summary>
    /// GET /api/v1/intentions?fired=true&amp;offset=0&amp;limit=50
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool? fired = null,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var query = _db.Intentions
            .Where(i => userIds.Contains(i.UserId));

        if (fired.HasValue)
            query = query.Where(i => i.Fired == fired.Value);

        var total = await query.CountAsync();

        var intentions = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(i => new
            {
                i.Id,
                i.Content,
                TriggerConditions = i.TriggerConditions,
                i.Priority,
                FireOnce = i.FireOnce,
                i.Fired,
                FiredAt = i.FiredAt,
                CreatedAt = i.CreatedAt,
                ExpiresAt = i.ExpiresAt
            })
            .ToListAsync();

        // Parse trigger_conditions from JSON string to object for each intention
        var result = intentions.Select(i => new
        {
            i.Id,
            i.Content,
            TriggerConditions = ParseJsonOrNull(i.TriggerConditions),
            i.Priority,
            i.FireOnce,
            i.Fired,
            i.FiredAt,
            i.CreatedAt,
            i.ExpiresAt
        });

        return Ok(new { intentions = result, total });
    }

    /// <summary>
    /// POST /api/v1/intentions
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateIntentionRequest request)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);
        var userId = userIds.FirstOrDefault() ?? user.Id;

        var intention = new Intention
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Content = request.Content,
            TriggerConditions = request.TriggerConditions?.GetRawText() ?? "{}",
            Priority = request.Priority,
            FireOnce = request.FireOnce,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = DateTime.UtcNow
        };

        _db.Intentions.Add(intention);
        await _db.SaveChangesAsync();

        return Ok(new { id = intention.Id, ok = true });
    }

    /// <summary>
    /// PUT /api/v1/intentions/{id} — partial update
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateIntentionRequest request)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var intention = await _db.Intentions
            .FirstOrDefaultAsync(i => i.Id == id && userIds.Contains(i.UserId));

        if (intention == null)
            return NotFound(new { error = "Intention not found" });

        if (request.Content != null)
            intention.Content = request.Content;

        if (request.TriggerConditions != null)
            intention.TriggerConditions = request.TriggerConditions.Value.GetRawText();

        if (request.Priority.HasValue)
            intention.Priority = request.Priority.Value;

        if (request.FireOnce.HasValue)
            intention.FireOnce = request.FireOnce.Value;

        if (request.ExpiresAt.HasValue)
            intention.ExpiresAt = request.ExpiresAt.Value;

        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    /// <summary>
    /// DELETE /api/v1/intentions/{id}
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var intention = await _db.Intentions
            .FirstOrDefaultAsync(i => i.Id == id && userIds.Contains(i.UserId));

        if (intention == null)
            return NotFound(new { error = "Intention not found" });

        _db.Intentions.Remove(intention);
        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    private static object? ParseJsonOrNull(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch
        {
            return json;
        }
    }
}

public class CreateIntentionRequest
{
    public string Content { get; set; } = null!;
    public JsonElement? TriggerConditions { get; set; }
    public int Priority { get; set; }
    public bool FireOnce { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
}

public class UpdateIntentionRequest
{
    public string? Content { get; set; }
    public JsonElement? TriggerConditions { get; set; }
    public int? Priority { get; set; }
    public bool? FireOnce { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
