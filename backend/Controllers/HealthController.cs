using Microsoft.AspNetCore.Mvc;

namespace GitXO.Api.Controllers;

[ApiController]
[Route("api")]
public class HealthController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "ok", message = "GitXO backend is running" });
    }
}
