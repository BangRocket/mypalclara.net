using Microsoft.AspNetCore.Mvc;

namespace MyPalClara.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ok", service = "clara-gateway-api" });
    }
}
