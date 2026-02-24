using System.Text.Json;
using MyPalClara.Api.Extensions;
using MyPalClara.Api.Services;
using MyPalClara.Data;
using MyPalClara.Memory;
using MyPalClara.Memory.Dynamics;
using MyPalClara.Memory.VectorStore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Api.Controllers;

[ApiController]
[Route("api/v1/memories")]
public class MemoriesController : ControllerBase
{
    private readonly ClaraDbContext _db;
    private readonly UserIdentityService _identity;
    private readonly IRookMemory _rook;

    public MemoriesController(ClaraDbContext db, UserIdentityService identity, IRookMemory rook)
    {
        _db = db;
        _identity = identity;
        _rook = rook;
    }

    /// <summary>
    /// GET /api/v1/memories — list memories with pagination and filters
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListMemories(
        [FromQuery] string? category = null,
        [FromQuery(Name = "is_key")] bool? isKey = null,
        [FromQuery] string sort = "created_at",
        [FromQuery] string order = "desc",
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);
        if (userIds.Count == 0)
            return Ok(new { memories = Array.Empty<object>(), total = 0, offset, limit });

        var allMemories = new List<MemoryPoint>();
        foreach (var uid in userIds)
        {
            var points = await _rook.GetAllAsync(userId: uid, limit: 10000);
            allMemories.AddRange(points);
        }

        // Enrich with dynamics data
        var memoryIds = allMemories.Select(m => m.Id).ToList();
        var dynamicsMap = await _db.MemoryDynamics
            .Where(d => memoryIds.Contains(d.MemoryId))
            .ToDictionaryAsync(d => d.MemoryId);

        var enriched = new List<object>();
        foreach (var m in allMemories)
        {
            dynamicsMap.TryGetValue(m.Id, out var dyn);

            // Apply filters
            if (category != null && dyn?.Category != category) continue;
            if (isKey.HasValue && (dyn?.IsKey ?? false) != isKey.Value) continue;

            enriched.Add(BuildMemoryResponse(m, dyn));
        }

        // Sort
        var sorted = order == "desc"
            ? enriched.OrderByDescending(e => GetSortKey(e, sort))
            : enriched.OrderBy(e => GetSortKey(e, sort));

        var total = enriched.Count;
        var paginated = sorted.Skip(offset).Take(limit).ToList();

        return Ok(new { memories = paginated, total, offset, limit });
    }

    /// <summary>
    /// GET /api/v1/memories/stats — aggregate stats
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var dynamics = await _db.MemoryDynamics
            .Where(md => userIds.Contains(md.UserId))
            .ToListAsync();

        var byCategory = new Dictionary<string, int>();
        var keyCount = 0;
        foreach (var d in dynamics)
        {
            var cat = d.Category ?? "uncategorized";
            byCategory[cat] = byCategory.GetValueOrDefault(cat) + 1;
            if (d.IsKey) keyCount++;
        }

        return Ok(new { total = dynamics.Count, by_category = byCategory, key_count = keyCount });
    }

    /// <summary>
    /// GET /api/v1/memories/{id} — single memory with full metadata
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetMemory(string id)
    {
        HttpContext.RequireApprovedUser();

        var point = await _rook.GetAsync(id);
        if (point == null)
            return NotFound(new { error = "Memory not found" });

        var dyn = await _db.MemoryDynamics
            .FirstOrDefaultAsync(d => d.MemoryId == id);

        return Ok(BuildMemoryResponse(point, dyn));
    }

    /// <summary>
    /// POST /api/v1/memories — create a new memory
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateMemory([FromBody] CreateMemoryRequest request)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);
        if (userIds.Count == 0)
            return BadRequest(new { error = "No platform linked" });

        var primaryUid = userIds[0];
        var memoryId = await _rook.CreateAsync(
            request.Content,
            primaryUid,
            isKey: request.IsKey,
            metadata: request.Metadata);

        // Set dynamics if provided
        if (request.Category != null || request.IsKey)
        {
            var dyn = await _db.MemoryDynamics.FirstOrDefaultAsync(d => d.MemoryId == memoryId);
            if (dyn != null)
            {
                if (request.Category != null) dyn.Category = request.Category;
                if (request.IsKey) dyn.IsKey = true;
                await _db.SaveChangesAsync();
            }
        }

        return Ok(new { ok = true, id = memoryId });
    }

    /// <summary>
    /// PUT /api/v1/memories/{id} — update memory content
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateMemory(string id, [FromBody] UpdateMemoryRequest request)
    {
        HttpContext.RequireApprovedUser();

        if (request.Content != null)
            await _rook.UpdateAsync(id, request.Content);

        if (request.Category != null || request.IsKey.HasValue)
        {
            var dyn = await _db.MemoryDynamics.FirstOrDefaultAsync(d => d.MemoryId == id);
            if (dyn != null)
            {
                if (request.Category != null) dyn.Category = request.Category;
                if (request.IsKey.HasValue) dyn.IsKey = request.IsKey.Value;
                await _db.SaveChangesAsync();
            }
        }

        return Ok(new { ok = true });
    }

    /// <summary>
    /// DELETE /api/v1/memories/{id} — delete memory
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMemory(string id)
    {
        HttpContext.RequireApprovedUser();
        await _rook.DeleteAsync(id);
        return Ok(new { ok = true });
    }

    /// <summary>
    /// GET /api/v1/memories/{id}/history — change history from memory_history table
    /// </summary>
    [HttpGet("{id}/history")]
    public async Task<IActionResult> History(string id)
    {
        HttpContext.RequireApprovedUser();

        var history = await _db.MemoryHistories
            .Where(mh => mh.MemoryId == id)
            .OrderByDescending(mh => mh.CreatedAt)
            .Select(mh => new
            {
                mh.Id,
                mh.Event,
                OldMemory = mh.OldMemory,
                NewMemory = mh.NewMemory,
                CreatedAt = mh.CreatedAt
            })
            .ToListAsync();

        return Ok(new { history });
    }

    /// <summary>
    /// GET /api/v1/memories/{id}/dynamics — FSRS dynamics state
    /// </summary>
    [HttpGet("{id}/dynamics")]
    public async Task<IActionResult> Dynamics(string id)
    {
        HttpContext.RequireApprovedUser();

        var dynamics = await _db.MemoryDynamics
            .FirstOrDefaultAsync(md => md.MemoryId == id);

        if (dynamics == null)
            return NotFound(new { error = "Dynamics not found" });

        var supersessions = await _db.MemorySupersessions
            .Where(ms => ms.OldMemoryId == id || ms.NewMemoryId == id)
            .Select(ms => new
            {
                ms.Id,
                OldMemoryId = ms.OldMemoryId,
                NewMemoryId = ms.NewMemoryId,
                ms.Reason,
                ms.Confidence
            })
            .ToListAsync();

        return Ok(new
        {
            memory_id = dynamics.MemoryId,
            dynamics.Stability,
            dynamics.Difficulty,
            retrieval_strength = dynamics.RetrievalStrength,
            storage_strength = dynamics.StorageStrength,
            is_key = dynamics.IsKey,
            importance_weight = dynamics.ImportanceWeight,
            dynamics.Category,
            access_count = dynamics.AccessCount,
            last_accessed_at = dynamics.LastAccessedAt,
            created_at = dynamics.CreatedAt,
            supersessions
        });
    }

    /// <summary>
    /// POST /api/v1/memories/search — semantic search
    /// </summary>
    [HttpPost("search")]
    public async Task<IActionResult> SearchMemories([FromBody] MemorySearchRequest request)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);
        if (userIds.Count == 0)
            return Ok(new { results = Array.Empty<object>() });

        var allResults = new List<MemorySearchResult>();
        foreach (var uid in userIds)
        {
            var results = await _rook.SearchAsync(
                request.Query, userId: uid, limit: request.Limit, threshold: request.Threshold);
            allResults.AddRange(results);
        }

        // Sort by score descending
        allResults.Sort((a, b) => b.Score.CompareTo(a.Score));

        // Enrich with dynamics
        var memoryIds = allResults.Select(r => r.Point.Id).ToList();
        var dynamicsMap = memoryIds.Count > 0
            ? await _db.MemoryDynamics
                .Where(d => memoryIds.Contains(d.MemoryId))
                .ToDictionaryAsync(d => d.MemoryId)
            : new Dictionary<string, Data.Entities.MemoryDynamics>();

        var enriched = new List<object>();
        foreach (var r in allResults.Take(request.Limit))
        {
            dynamicsMap.TryGetValue(r.Point.Id, out var dyn);

            if (request.Category != null && dyn?.Category != request.Category) continue;
            if (request.IsKey.HasValue && (dyn?.IsKey ?? false) != request.IsKey.Value) continue;

            enriched.Add(new
            {
                id = r.Point.Id,
                content = r.Point.Data,
                score = r.Score,
                metadata = r.Point.Metadata,
                dynamics = dyn != null ? new
                {
                    is_key = dyn.IsKey,
                    category = dyn.Category,
                    stability = dyn.Stability,
                } : null
            });
        }

        return Ok(new { results = enriched });
    }

    /// <summary>
    /// PUT /api/v1/memories/{id}/tags — update tags
    /// </summary>
    [HttpPut("{id}/tags")]
    public async Task<IActionResult> UpdateTags(string id, [FromBody] UpdateTagsRequest request)
    {
        HttpContext.RequireApprovedUser();

        var dynamics = await _db.MemoryDynamics
            .FirstOrDefaultAsync(md => md.MemoryId == id);

        if (dynamics == null)
            return NotFound(new { error = "Memory dynamics not found" });

        dynamics.Tags = JsonSerializer.Serialize(request.Tags);
        dynamics.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, tags = request.Tags });
    }

    /// <summary>
    /// GET /api/v1/memories/tags/all — distinct tags
    /// </summary>
    [HttpGet("tags/all")]
    public async Task<IActionResult> AllTags()
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);

        var tagStrings = await _db.MemoryDynamics
            .Where(md => userIds.Contains(md.UserId) && md.Tags != null)
            .Select(md => md.Tags!)
            .ToListAsync();

        var allTags = new HashSet<string>();
        foreach (var tagJson in tagStrings)
        {
            try
            {
                var tags = JsonSerializer.Deserialize<List<string>>(tagJson);
                if (tags != null)
                    foreach (var tag in tags)
                        allTags.Add(tag);
            }
            catch { /* Skip malformed JSON */ }
        }

        return Ok(new { tags = allTags.OrderBy(t => t).ToList() });
    }

    /// <summary>
    /// GET /api/v1/memories/export — export all memories as JSON
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportMemories()
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);
        if (userIds.Count == 0)
            return File(
                System.Text.Encoding.UTF8.GetBytes("""{"memories": []}"""),
                "application/json",
                "clara-memories-export.json");

        var allMemories = new List<MemoryPoint>();
        foreach (var uid in userIds)
        {
            var points = await _rook.GetAllAsync(userId: uid, limit: 10000);
            allMemories.AddRange(points);
        }

        var memoryIds = allMemories.Select(m => m.Id).ToList();
        var dynamicsMap = memoryIds.Count > 0
            ? await _db.MemoryDynamics
                .Where(d => memoryIds.Contains(d.MemoryId))
                .ToDictionaryAsync(d => d.MemoryId)
            : new Dictionary<string, Data.Entities.MemoryDynamics>();

        var exportData = allMemories.Select(m =>
        {
            dynamicsMap.TryGetValue(m.Id, out var dyn);
            List<string> tags = [];
            if (dyn?.Tags != null)
            {
                try { tags = JsonSerializer.Deserialize<List<string>>(dyn.Tags) ?? []; }
                catch { /* skip */ }
            }
            return new
            {
                id = m.Id,
                content = m.Data,
                metadata = m.Metadata,
                category = dyn?.Category,
                is_key = dyn?.IsKey ?? false,
                tags,
                created_at = m.CreatedAt
            };
        }).ToList();

        var payload = JsonSerializer.Serialize(new
        {
            memories = exportData,
            exported_at = DateTime.UtcNow.ToString("O")
        }, new JsonSerializerOptions { WriteIndented = true });

        return File(
            System.Text.Encoding.UTF8.GetBytes(payload),
            "application/json",
            "clara-memories-export.json");
    }

    /// <summary>
    /// POST /api/v1/memories/import — import memories from JSON
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> ImportMemories([FromBody] MemoryImportRequest request)
    {
        var user = HttpContext.RequireApprovedUser();
        var userIds = await _identity.ResolveAllUserIds(user.Id);
        if (userIds.Count == 0)
            return BadRequest(new { error = "No platform linked" });

        var primaryUid = userIds[0];
        var imported = 0;

        foreach (var item in request.Memories)
        {
            try
            {
                var memoryId = await _rook.CreateAsync(
                    item.Content, primaryUid, isKey: item.IsKey, metadata: item.Metadata);

                var dyn = await _db.MemoryDynamics.FirstOrDefaultAsync(d => d.MemoryId == memoryId);
                if (dyn != null)
                {
                    if (item.Category != null) dyn.Category = item.Category;
                    if (item.IsKey) dyn.IsKey = true;
                    if (item.Tags.Count > 0) dyn.Tags = JsonSerializer.Serialize(item.Tags);
                    await _db.SaveChangesAsync();
                }
                imported++;
            }
            catch { /* skip failed imports */ }
        }

        return Ok(new { ok = true, imported, total = request.Memories.Count });
    }

    // --- Helpers ---

    private static object BuildMemoryResponse(MemoryPoint m, Data.Entities.MemoryDynamics? dyn)
    {
        return new
        {
            id = m.Id,
            content = m.Data,
            metadata = m.Metadata,
            created_at = m.CreatedAt,
            updated_at = m.UpdatedAt,
            user_id = m.UserId,
            dynamics = dyn != null ? new
            {
                stability = dyn.Stability,
                difficulty = dyn.Difficulty,
                retrieval_strength = dyn.RetrievalStrength,
                storage_strength = dyn.StorageStrength,
                is_key = dyn.IsKey,
                category = dyn.Category,
                access_count = dyn.AccessCount,
                last_accessed_at = dyn.LastAccessedAt,
            } : null
        };
    }

    private static object? GetSortKey(object item, string sort)
    {
        // Use reflection-free approach via anonymous type properties
        var type = item.GetType();
        var prop = sort switch
        {
            "stability" => type.GetProperty("dynamics")?.GetValue(item) is { } dyn
                ? dyn.GetType().GetProperty("stability")?.GetValue(dyn)
                : null,
            "access_count" => type.GetProperty("dynamics")?.GetValue(item) is { } dyn2
                ? dyn2.GetType().GetProperty("access_count")?.GetValue(dyn2)
                : null,
            _ => type.GetProperty(sort)?.GetValue(item)
        };
        return prop;
    }
}

// --- Request models ---

public class CreateMemoryRequest
{
    public string Content { get; set; } = null!;
    public string? Category { get; set; }
    public bool IsKey { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class UpdateMemoryRequest
{
    public string? Content { get; set; }
    public string? Category { get; set; }
    public bool? IsKey { get; set; }
}

public class MemorySearchRequest
{
    public string Query { get; set; } = null!;
    public string? Category { get; set; }
    public bool? IsKey { get; set; }
    public int Limit { get; set; } = 20;
    public float Threshold { get; set; } = 0.0f;
}

public class UpdateTagsRequest
{
    public List<string> Tags { get; set; } = [];
}

public class MemoryImportItem
{
    public string Content { get; set; } = null!;
    public string? Category { get; set; }
    public bool IsKey { get; set; }
    public List<string> Tags { get; set; } = [];
    public Dictionary<string, object>? Metadata { get; set; }
}

public class MemoryImportRequest
{
    public List<MemoryImportItem> Memories { get; set; } = [];
}
