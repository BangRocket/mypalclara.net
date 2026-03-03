using Microsoft.AspNetCore.Mvc;

namespace Clara.Gateway.Api;

[ApiController]
[Route("api/v1")]
public class HealthController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult GetHealth() => Ok(new
    {
        status = "ok",
        timestamp = DateTime.UtcNow,
    });
}
