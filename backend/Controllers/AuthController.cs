using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using GitXO.Api.Services;

namespace GitXO.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly ActivityLogger _logger;

    public AuthController(AuthService auth, ActivityLogger logger)
    {
        _auth = auth;
        _logger = logger;
    }

    // POST /api/auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest body)
    {
        var (success, error, user, token) = await _auth.RegisterAsync(
            body.Username, body.Email, body.Password);

        if (!success)
            return BadRequest(new { error });

        _logger.LogEvent("USER_REGISTERED", null, new { username = user!.Username });
        return StatusCode(201, new
        {
            token,
            user = new { user.Id, user.Username, user.Email, user.IsAdmin }
        });
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body)
    {
        var (success, error, user, token) = await _auth.LoginAsync(body.Email, body.Password);

        if (!success)
            return Unauthorized(new { error });

        _logger.LogEvent("USER_LOGIN", null, new { username = user!.Username });
        return Ok(new
        {
            token,
            user = new { user.Id, user.Username, user.Email, user.IsAdmin }
        });
    }

    // GET /api/auth/me
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var id))
            return Unauthorized(new { error = "Invalid token" });

        var user = await _auth.GetUserByIdAsync(id);
        if (user == null)
            return NotFound(new { error = "User not found" });

        return Ok(new
        {
            user.Id,
            user.Username,
            user.Email,
            user.IsAdmin,
            user.DisplayName,
            user.Bio
        });
    }
}

public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string Email, string Password);
