using Microsoft.AspNetCore.Mvc;
using MyPalClara.Modules.Graph.Api;

namespace MyPalClara.Api.Controllers;

[ApiController]
[Route("api/v1/graph")]
public class GraphController : ControllerBase
{
    private readonly GraphApiService? _graphService;

    public GraphController(IServiceProvider services)
    {
        _graphService = services.GetService<GraphApiService>();
    }

    [HttpGet("entities")]
    public async Task<IActionResult> GetEntities([FromQuery] string? type, [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (_graphService is null) return StatusCode(503, new { error = "Graph module not available" });
        var entities = await _graphService.GetEntitiesAsync(type, limit, ct);
        return Ok(new { entities });
    }

    [HttpGet("relationships")]
    public async Task<IActionResult> GetRelationships([FromQuery] string? type, [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (_graphService is null) return StatusCode(503, new { error = "Graph module not available" });
        var rels = await _graphService.GetRelationshipsAsync(type, limit, ct);
        return Ok(new { relationships = rels });
    }

    [HttpPost("search")]
    public IActionResult Search([FromBody] object query)
    {
        if (_graphService is null) return StatusCode(503, new { error = "Graph module not available" });
        return Ok(new { results = Array.Empty<object>() });
    }
}
