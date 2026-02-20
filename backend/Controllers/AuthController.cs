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
    // Open when no users exist (first admin); otherwise requires an existing admin.
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest body)
    {
        var callerIsAdmin = User.Identity?.IsAuthenticated == true &&
                            User.FindFirstValue("is_admin") == "true";

        var totalUsers = await _auth.GetTotalUsersAsync();
        if (totalUsers > 0 && !callerIsAdmin)
            return Unauthorized(new { error = "Registration requires an administrator account" });

        var (success, error, user, _) = await _auth.RegisterAsync(body.Username, body.Email, body.Password);
        if (!success)
            return BadRequest(new { error });

        var createdBy = User.FindFirstValue("username") ?? "system";
        _logger.LogEvent("USER_CREATED", null, new { username = user!.Username, createdBy });
        return StatusCode(201, new { user = new { user.Id, user.Username, user.Email, user.IsAdmin } });
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body)
    {
        var (success, error, user, accessToken) = await _auth.LoginAsync(body.Email, body.Password);
        if (!success)
            return Unauthorized(new { error });

        var userAgent = Request.Headers.UserAgent.ToString();
        var refreshToken = await _auth.CreateRefreshTokenAsync(user!.Id, userAgent);

        _logger.LogEvent("USER_LOGIN", null, new { username = user.Username }, user.Id);
        return Ok(new
        {
            accessToken,
            refreshToken,
            user = new { user.Id, user.Username, user.Email, user.IsAdmin, user.DisplayName, user.AvatarUrl }
        });
    }

    // POST /api/auth/refresh  — exchange a refresh token for a new access+refresh pair
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest body)
    {
        if (string.IsNullOrEmpty(body.RefreshToken))
            return BadRequest(new { error = "refreshToken required" });

        var userAgent = Request.Headers.UserAgent.ToString();
        var (success, error, user, accessToken, newRefreshToken) =
            await _auth.RefreshAsync(body.RefreshToken, userAgent);

        if (!success)
            return Unauthorized(new { error });

        return Ok(new
        {
            accessToken,
            refreshToken = newRefreshToken,
            user = new { user!.Id, user.Username, user.Email, user.IsAdmin, user.DisplayName, user.AvatarUrl }
        });
    }

    // POST /api/auth/logout  — revoke one or all refresh tokens
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? body)
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var userId))
            return Unauthorized();

        if (!string.IsNullOrEmpty(body?.RefreshToken))
            await _auth.RevokeRefreshTokenAsync(body.RefreshToken);
        else
            await _auth.RevokeAllSessionsAsync(userId);

        _logger.LogEvent("USER_LOGOUT", null, new { }, userId);
        return Ok(new { message = "Signed out successfully" });
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
            user.Bio,
            user.AvatarUrl
        });
    }

    // PUT /api/auth/profile
    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest body)
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var userId))
            return Unauthorized();

        var (success, error) = await _auth.UpdateProfileAsync(userId, body.DisplayName, body.Bio, body.AvatarUrl);
        if (!success) return BadRequest(new { error });

        var user = await _auth.GetUserByIdAsync(userId);
        _logger.LogEvent("PROFILE_UPDATED", null, new { }, userId);
        return Ok(new
        {
            user = new { user!.Id, user.Username, user.Email, user.IsAdmin, user.DisplayName, user.Bio, user.AvatarUrl }
        });
    }

    // PUT /api/auth/password
    [HttpPut("password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest body)
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var userId))
            return Unauthorized();

        var (success, error) = await _auth.ChangePasswordAsync(userId, body.CurrentPassword, body.NewPassword);
        if (!success) return BadRequest(new { error });

        // Revoke all sessions after password change for security
        await _auth.RevokeAllSessionsAsync(userId);
        _logger.LogEvent("PASSWORD_CHANGED", null, new { }, userId);
        return Ok(new { message = "Password changed. All sessions have been revoked — please sign in again." });
    }

    // PUT /api/auth/email
    [HttpPut("email")]
    [Authorize]
    public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest body)
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var userId))
            return Unauthorized();

        var (success, error) = await _auth.ChangeEmailAsync(userId, body.CurrentPassword, body.NewEmail);
        if (!success) return BadRequest(new { error });

        _logger.LogEvent("EMAIL_CHANGED", null, new { }, userId);
        return Ok(new { message = "Email address updated" });
    }

    // GET /api/auth/settings
    [HttpGet("settings")]
    [Authorize]
    public async Task<IActionResult> GetSettings()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var userId))
            return Unauthorized();

        var settings = await _auth.GetUserSettingsAsync(userId);
        return Ok(settings);
    }

    // PUT /api/auth/settings
    [HttpPut("settings")]
    [Authorize]
    public async Task<IActionResult> UpdateSettings([FromBody] UserSettings body)
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var userId))
            return Unauthorized();

        body.UserId = userId;
        var ok = await _auth.UpdateUserSettingsAsync(userId, body);
        if (!ok) return StatusCode(500, new { error = "Failed to save settings" });

        return Ok(body);
    }

    // GET /api/auth/sessions
    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> GetSessions()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var userId))
            return Unauthorized();

        var sessions = await _auth.GetActiveSessionsAsync(userId);
        return Ok(sessions);
    }

    // DELETE /api/auth/sessions/{id}
    [HttpDelete("sessions/{id:long}")]
    [Authorize]
    public async Task<IActionResult> RevokeSession(long id)
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var userId))
            return Unauthorized();

        var ok = await _auth.RevokeSessionByIdAsync(id, userId);
        if (!ok) return NotFound(new { error = "Session not found" });

        _logger.LogEvent("SESSION_REVOKED", null, new { sessionId = id }, userId);
        return Ok(new { message = "Session revoked" });
    }

    // GET /api/auth/users  (admin only)
    [HttpGet("users")]
    [Authorize]
    public async Task<IActionResult> ListUsers()
    {
        if (User.FindFirstValue("is_admin") != "true")
            return Forbid();

        var users = await _auth.ListUsersAsync();
        return Ok(users);
    }

    // DELETE /api/auth/users/{id}  (admin only)
    [HttpDelete("users/{id:long}")]
    [Authorize]
    public async Task<IActionResult> DeleteUser(long id)
    {
        if (User.FindFirstValue("is_admin") != "true")
            return Forbid();

        var myIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(myIdStr, out var myId) && id == myId)
            return BadRequest(new { error = "You cannot delete your own account" });

        var ok = await _auth.DeleteUserAsync(id);
        if (!ok) return NotFound(new { error = "User not found" });

        _logger.LogEvent("USER_DELETED", null, new { deletedUserId = id });
        return Ok(new { message = "User deleted" });
    }
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Username, string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string? RefreshToken);
public record UpdateProfileRequest(string? DisplayName, string? Bio, string? AvatarUrl);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record ChangeEmailRequest(string CurrentPassword, string NewEmail);
