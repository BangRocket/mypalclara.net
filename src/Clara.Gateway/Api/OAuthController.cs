using Microsoft.AspNetCore.Mvc;

namespace Clara.Gateway.Api;

[ApiController]
[Route("api/v1/oauth")]
public class OAuthController : ControllerBase
{
    [HttpGet("callback")]
    public IActionResult Callback(
        [FromQuery] string? code = null,
        [FromQuery] string? state = null,
        [FromQuery] string? error = null)
    {
        if (!string.IsNullOrEmpty(error))
            return BadRequest(new { error, message = "OAuth authorization denied" });

        if (string.IsNullOrEmpty(code))
            return BadRequest(new { error = "missing_code", message = "No authorization code provided" });

        // TODO: Exchange code for token, store in database
        return Ok(new
        {
            status = "ok",
            message = "OAuth callback received",
        });
    }
}
