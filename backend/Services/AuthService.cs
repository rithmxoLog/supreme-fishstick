using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace GitXO.Api.Services;

public class AuthService
{
    private readonly string _connectionString;
    private readonly string _jwtSecret;
    private readonly string _jwtIssuer;
    private readonly string _jwtAudience;
    private readonly int _jwtExpiryHours;
    private readonly int _refreshTokenExpiryDays;

    public AuthService(IConfiguration config)
    {
        var pg = config.GetSection("Postgres");
        _connectionString =
            $"Host={pg["Host"] ?? "localhost"};" +
            $"Port={pg["Port"] ?? "5432"};" +
            $"Database={pg["Database"] ?? "gitxo"};" +
            $"Username={pg["Username"] ?? "postgres"};" +
            $"Password={pg["Password"] ?? ""}";

        var jwt = config.GetSection("Jwt");
        _jwtSecret = jwt["Secret"] ?? "gitxo-default-secret-change-this-now-32chars";
        _jwtIssuer = jwt["Issuer"] ?? "GitXO";
        _jwtAudience = jwt["Audience"] ?? "GitXO";
        _jwtExpiryHours = int.TryParse(jwt["ExpiryHours"], out var h) ? h : 1;
        _refreshTokenExpiryDays = int.TryParse(jwt["RefreshTokenExpiryDays"], out var d) ? d : 30;
    }

    // ── Register ──────────────────────────────────────────────

    public async Task<(bool Success, string? Error, UserInfo? User, string? Token)> RegisterAsync(
        string username, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || !Regex.IsMatch(username, @"^[a-zA-Z0-9_\-]{3,30}$"))
            return (false, "Username must be 3–30 alphanumeric characters, dashes, or underscores", null, null);
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return (false, "Invalid email address", null, null);
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            return (false, "Password must be at least 12 characters", null, null);

        var hash = BCrypt.Net.BCrypt.HashPassword(password);

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM users WHERE username = $1 OR email = $2";
            checkCmd.Parameters.Add(new NpgsqlParameter { Value = username });
            checkCmd.Parameters.Add(new NpgsqlParameter { Value = email });
            var count = (long)(await checkCmd.ExecuteScalarAsync())!;
            if (count > 0)
                return (false, "Username or email already taken", null, null);

            await using var totalCmd = conn.CreateCommand();
            totalCmd.CommandText = "SELECT COUNT(*) FROM users";
            var totalUsers = (long)(await totalCmd.ExecuteScalarAsync())!;
            var isAdmin = totalUsers == 0;

            await using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO users (username, email, password_hash, is_admin)
                VALUES ($1, $2, $3, $4)
                RETURNING id, username, email, is_admin, created_at";
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = username });
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = email });
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = hash });
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = isAdmin });

            await using var reader = await insertCmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var user = new UserInfo
            {
                Id = reader.GetInt64(0),
                Username = reader.GetString(1),
                Email = reader.GetString(2),
                IsAdmin = reader.GetBoolean(3),
                CreatedAt = reader.GetDateTime(4)
            };

            return (true, null, user, GenerateAccessToken(user));
        }
        catch (Exception ex)
        {
            return (false, $"Registration failed: {ex.Message}", null, null);
        }
    }

    // ── Login ─────────────────────────────────────────────────

    private const int MaxFailedAttempts = 10;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<(bool Success, string? Error, UserInfo? User, string? Token)> LoginAsync(
        string email, string password)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, email, password_hash, is_admin, created_at,
                       failed_login_attempts, locked_until
                FROM users WHERE email = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = email });

            long id; string username, storedHash; bool isAdmin; DateTime createdAt;
            int failedAttempts; DateTime? lockedUntil;

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return (false, "Invalid email or password", null, null);

                id            = reader.GetInt64(0);
                username      = reader.GetString(1);
                storedHash    = reader.GetString(3);
                isAdmin       = reader.GetBoolean(4);
                createdAt     = reader.GetDateTime(5);
                failedAttempts = reader.GetInt32(6);
                lockedUntil   = reader.IsDBNull(7) ? null : reader.GetDateTime(7);
            }

            // Check account lockout
            if (lockedUntil.HasValue && lockedUntil.Value > DateTime.UtcNow)
            {
                var mins = (int)Math.Ceiling((lockedUntil.Value - DateTime.UtcNow).TotalMinutes);
                return (false, $"Account temporarily locked. Try again in {mins} minute(s).", null, null);
            }

            if (!BCrypt.Net.BCrypt.Verify(password, storedHash))
            {
                var newFailed = failedAttempts + 1;
                DateTime? newLock = newFailed >= MaxFailedAttempts
                    ? DateTime.UtcNow.Add(LockoutDuration)
                    : null;

                await using var failCmd = conn.CreateCommand();
                failCmd.CommandText = @"
                    UPDATE users SET failed_login_attempts = $2, locked_until = $3 WHERE id = $1";
                failCmd.Parameters.Add(new NpgsqlParameter { Value = id });
                failCmd.Parameters.Add(new NpgsqlParameter { Value = newFailed });
                failCmd.Parameters.Add(new NpgsqlParameter { Value = (object?)newLock ?? DBNull.Value });
                await failCmd.ExecuteNonQueryAsync();

                return (false, "Invalid email or password", null, null);
            }

            // Successful login — reset lockout counters
            await using var resetCmd = conn.CreateCommand();
            resetCmd.CommandText = @"
                UPDATE users SET failed_login_attempts = 0, locked_until = NULL WHERE id = $1";
            resetCmd.Parameters.Add(new NpgsqlParameter { Value = id });
            await resetCmd.ExecuteNonQueryAsync();

            var user = new UserInfo
            {
                Id        = id,
                Username  = username,
                Email     = email,
                IsAdmin   = isAdmin,
                CreatedAt = createdAt
            };

            return (true, null, user, GenerateAccessToken(user));
        }
        catch (Exception ex)
        {
            return (false, $"Login failed: {ex.Message}", null, null);
        }
    }

    // ── Refresh Tokens ────────────────────────────────────────

    public async Task<string> CreateRefreshTokenAsync(long userId, string? userAgent)
    {
        var rawToken = GenerateSecureToken();
        var tokenHash = HashToken(rawToken);
        var expiresAt = DateTime.UtcNow.AddDays(_refreshTokenExpiryDays);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO refresh_tokens (user_id, token_hash, expires_at, user_agent)
            VALUES ($1, $2, $3, $4)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = userId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = tokenHash });
        cmd.Parameters.Add(new NpgsqlParameter { Value = expiresAt });
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)userAgent ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync();

        return rawToken;
    }

    public async Task<(bool Success, string? Error, UserInfo? User, string? AccessToken, string? RefreshToken)>
        RefreshAsync(string rawRefreshToken, string? userAgent)
    {
        var tokenHash = HashToken(rawRefreshToken);
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var findCmd = conn.CreateCommand();
            findCmd.CommandText = @"
                SELECT id, user_id FROM refresh_tokens
                WHERE token_hash = $1
                  AND revoked_at IS NULL
                  AND expires_at > NOW()";
            findCmd.Parameters.Add(new NpgsqlParameter { Value = tokenHash });

            long tokenId, userId;
            await using (var reader = await findCmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return (false, "Invalid or expired refresh token", null, null, null);
                tokenId = reader.GetInt64(0);
                userId = reader.GetInt64(1);
            }

            // Rotate: revoke old, issue new
            await using var revokeCmd = conn.CreateCommand();
            revokeCmd.CommandText = "UPDATE refresh_tokens SET revoked_at = NOW() WHERE id = $1";
            revokeCmd.Parameters.Add(new NpgsqlParameter { Value = tokenId });
            await revokeCmd.ExecuteNonQueryAsync();

            var user = await GetUserByIdAsync(userId);
            if (user == null) return (false, "User not found", null, null, null);

            var newAccessToken = GenerateAccessToken(user);
            var newRefreshToken = await CreateRefreshTokenAsync(userId, userAgent);

            return (true, null, user, newAccessToken, newRefreshToken);
        }
        catch (Exception ex)
        {
            return (false, $"Token refresh failed: {ex.Message}", null, null, null);
        }
    }

    public async Task<bool> RevokeRefreshTokenAsync(string rawRefreshToken)
    {
        var tokenHash = HashToken(rawRefreshToken);
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE refresh_tokens SET revoked_at = NOW() WHERE token_hash = $1 AND revoked_at IS NULL";
            cmd.Parameters.Add(new NpgsqlParameter { Value = tokenHash });
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> RevokeAllSessionsAsync(long userId)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE refresh_tokens SET revoked_at = NOW() WHERE user_id = $1 AND revoked_at IS NULL";
            cmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<List<SessionInfo>> GetActiveSessionsAsync(long userId)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, user_agent, created_at, expires_at
                FROM refresh_tokens
                WHERE user_id = $1
                  AND revoked_at IS NULL
                  AND expires_at > NOW()
                ORDER BY created_at DESC";
            cmd.Parameters.Add(new NpgsqlParameter { Value = userId });

            var sessions = new List<SessionInfo>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sessions.Add(new SessionInfo
                {
                    Id = reader.GetInt64(0),
                    UserAgent = reader.IsDBNull(1) ? null : reader.GetString(1),
                    CreatedAt = reader.GetDateTime(2),
                    ExpiresAt = reader.GetDateTime(3)
                });
            }
            return sessions;
        }
        catch { return []; }
    }

    public async Task<bool> RevokeSessionByIdAsync(long sessionId, long requestingUserId)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE refresh_tokens SET revoked_at = NOW()
                WHERE id = $1 AND user_id = $2 AND revoked_at IS NULL";
            cmd.Parameters.Add(new NpgsqlParameter { Value = sessionId });
            cmd.Parameters.Add(new NpgsqlParameter { Value = requestingUserId });
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch { return false; }
    }

    // ── Profile / Account ─────────────────────────────────────

    public async Task<(bool Success, string? Error)> UpdateProfileAsync(
        long userId, string? displayName, string? bio, string? avatarUrl)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE users
                SET display_name = $2, bio = $3, avatar_url = $4, updated_at = NOW()
                WHERE id = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)displayName ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)bio ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)avatarUrl ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync();
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(
        long userId, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return (false, "New password must be at least 8 characters");

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var getCmd = conn.CreateCommand();
            getCmd.CommandText = "SELECT password_hash FROM users WHERE id = $1";
            getCmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            var storedHash = (string?)(await getCmd.ExecuteScalarAsync());
            if (storedHash == null) return (false, "User not found");

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, storedHash))
                return (false, "Current password is incorrect");

            var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = @"
                UPDATE users
                SET password_hash = $2, updated_at = NOW(),
                    failed_login_attempts = 0, locked_until = NULL
                WHERE id = $1";
            updateCmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            updateCmd.Parameters.Add(new NpgsqlParameter { Value = newHash });
            await updateCmd.ExecuteNonQueryAsync();
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Success, string? Error)> ChangeEmailAsync(
        long userId, string currentPassword, string newEmail)
    {
        if (string.IsNullOrWhiteSpace(newEmail) || !newEmail.Contains('@'))
            return (false, "Invalid email address");

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var getCmd = conn.CreateCommand();
            getCmd.CommandText = "SELECT password_hash FROM users WHERE id = $1";
            getCmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            var storedHash = (string?)(await getCmd.ExecuteScalarAsync());
            if (storedHash == null) return (false, "User not found");

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, storedHash))
                return (false, "Current password is incorrect");

            await using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM users WHERE email = $1 AND id != $2";
            checkCmd.Parameters.Add(new NpgsqlParameter { Value = newEmail });
            checkCmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            var taken = (long)(await checkCmd.ExecuteScalarAsync())!;
            if (taken > 0) return (false, "Email address already in use");

            await using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = "UPDATE users SET email = $2, updated_at = NOW() WHERE id = $1";
            updateCmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            updateCmd.Parameters.Add(new NpgsqlParameter { Value = newEmail });
            await updateCmd.ExecuteNonQueryAsync();
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ── User Settings ─────────────────────────────────────────

    public async Task<UserSettings> GetUserSettingsAsync(long userId)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var upsertCmd = conn.CreateCommand();
            upsertCmd.CommandText = "INSERT INTO user_settings (user_id) VALUES ($1) ON CONFLICT (user_id) DO NOTHING";
            upsertCmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            await upsertCmd.ExecuteNonQueryAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT user_id, theme, default_branch, show_email, email_on_push, email_on_issue FROM user_settings WHERE user_id = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = userId });

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return new UserSettings { UserId = userId };

            return new UserSettings
            {
                UserId = reader.GetInt64(0),
                Theme = reader.GetString(1),
                DefaultBranch = reader.GetString(2),
                ShowEmail = reader.GetBoolean(3),
                EmailOnPush = reader.GetBoolean(4),
                EmailOnIssue = reader.GetBoolean(5)
            };
        }
        catch { return new UserSettings { UserId = userId }; }
    }

    public async Task<bool> UpdateUserSettingsAsync(long userId, UserSettings settings)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO user_settings (user_id, theme, default_branch, show_email, email_on_push, email_on_issue)
                VALUES ($1, $2, $3, $4, $5, $6)
                ON CONFLICT (user_id) DO UPDATE SET
                    theme          = EXCLUDED.theme,
                    default_branch = EXCLUDED.default_branch,
                    show_email     = EXCLUDED.show_email,
                    email_on_push  = EXCLUDED.email_on_push,
                    email_on_issue = EXCLUDED.email_on_issue,
                    updated_at     = NOW()";
            cmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            cmd.Parameters.Add(new NpgsqlParameter { Value = settings.Theme });
            cmd.Parameters.Add(new NpgsqlParameter { Value = settings.DefaultBranch });
            cmd.Parameters.Add(new NpgsqlParameter { Value = settings.ShowEmail });
            cmd.Parameters.Add(new NpgsqlParameter { Value = settings.EmailOnPush });
            cmd.Parameters.Add(new NpgsqlParameter { Value = settings.EmailOnIssue });
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    // ── User Management (admin) ────────────────────────────────

    public async Task<long> GetTotalUsersAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            return (long)(await cmd.ExecuteScalarAsync())!;
        }
        catch { return 1; }
    }

    public async Task<List<UserInfo>> ListUsersAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, email, is_admin, created_at, display_name, bio, avatar_url
                FROM users ORDER BY created_at";

            var users = new List<UserInfo>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                users.Add(new UserInfo
                {
                    Id = reader.GetInt64(0),
                    Username = reader.GetString(1),
                    Email = reader.GetString(2),
                    IsAdmin = reader.GetBoolean(3),
                    CreatedAt = reader.GetDateTime(4),
                    DisplayName = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Bio = reader.IsDBNull(6) ? null : reader.GetString(6),
                    AvatarUrl = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }
            return users;
        }
        catch { return []; }
    }

    public async Task<bool> DeleteUserAsync(long userId)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM users WHERE id = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch { return false; }
    }

    // ── Git HTTP Basic Auth ───────────────────────────────────

    /// <summary>Verifies a GitXO username + plaintext password for git Basic auth.</summary>
    public async Task<(bool Success, UserInfo? User)> VerifyUsernamePasswordAsync(string username, string password)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, email, password_hash, is_admin, created_at
                FROM users WHERE username = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = username });

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return (false, null);

            var storedHash = reader.GetString(3);
            if (!BCrypt.Net.BCrypt.Verify(password, storedHash)) return (false, null);

            return (true, new UserInfo
            {
                Id = reader.GetInt64(0),
                Username = reader.GetString(1),
                Email = reader.GetString(2),
                IsAdmin = reader.GetBoolean(4),
                CreatedAt = reader.GetDateTime(5)
            });
        }
        catch { return (false, null); }
    }

    // ── User Lookup ───────────────────────────────────────────

    public async Task<UserInfo?> GetUserByIdAsync(long id)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, email, is_admin, created_at, display_name, bio, avatar_url
                FROM users WHERE id = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = id });

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new UserInfo
            {
                Id = reader.GetInt64(0),
                Username = reader.GetString(1),
                Email = reader.GetString(2),
                IsAdmin = reader.GetBoolean(3),
                CreatedAt = reader.GetDateTime(4),
                DisplayName = reader.IsDBNull(5) ? null : reader.GetString(5),
                Bio = reader.IsDBNull(6) ? null : reader.GetString(6),
                AvatarUrl = reader.IsDBNull(7) ? null : reader.GetString(7)
            };
        }
        catch { return null; }
    }

    // ── Token Helpers ─────────────────────────────────────────

    public string GenerateAccessToken(UserInfo user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("username", user.Username),
            new Claim("is_admin", user.IsAdmin.ToString().ToLower()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtIssuer,
            audience: _jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_jwtExpiryHours),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateToken(UserInfo user) => GenerateAccessToken(user);

    private static string GenerateSecureToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public class UserInfo
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
}

public class SessionInfo
{
    public long Id { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class UserSettings
{
    public long UserId { get; set; }
    public string Theme { get; set; } = "dark";
    public string DefaultBranch { get; set; } = "main";
    public bool ShowEmail { get; set; }
    public bool EmailOnPush { get; set; }
    public bool EmailOnIssue { get; set; }
}
