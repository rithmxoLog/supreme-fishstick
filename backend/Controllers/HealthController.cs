using Microsoft.AspNetCore.Mvc;

namespace GitXO.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("api/health")]
    [HttpGet("health")]
    [HttpGet("health/ready")]
    [HttpGet("health/live")]
    public IActionResult Health()
    {
        return Ok(new { status = "ok", message = "GitXO backend is running" });
    }
}
