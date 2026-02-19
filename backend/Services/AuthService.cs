using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
        _jwtExpiryHours = int.TryParse(jwt["ExpiryHours"], out var h) ? h : 168;
    }

    public async Task<(bool Success, string? Error, UserInfo? User, string? Token)> RegisterAsync(
        string username, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || !Regex.IsMatch(username, @"^[a-zA-Z0-9_\-]{3,30}$"))
            return (false, "Username must be 3–30 alphanumeric characters, dashes, or underscores", null, null);
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return (false, "Invalid email address", null, null);
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return (false, "Password must be at least 8 characters", null, null);

        var hash = BCrypt.Net.BCrypt.HashPassword(password);

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Check for existing username/email
            await using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM users WHERE username = $1 OR email = $2";
            checkCmd.Parameters.Add(new NpgsqlParameter { Value = username });
            checkCmd.Parameters.Add(new NpgsqlParameter { Value = email });
            var count = (long)(await checkCmd.ExecuteScalarAsync())!;
            if (count > 0)
                return (false, "Username or email already taken", null, null);

            // First user becomes admin
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

            return (true, null, user, GenerateToken(user));
        }
        catch (Exception ex)
        {
            return (false, $"Registration failed: {ex.Message}", null, null);
        }
    }

    public async Task<(bool Success, string? Error, UserInfo? User, string? Token)> LoginAsync(
        string email, string password)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, username, email, password_hash, is_admin, created_at FROM users WHERE email = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = email });

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return (false, "Invalid email or password", null, null);

            var storedHash = reader.GetString(3);
            if (!BCrypt.Net.BCrypt.Verify(password, storedHash))
                return (false, "Invalid email or password", null, null);

            var user = new UserInfo
            {
                Id = reader.GetInt64(0),
                Username = reader.GetString(1),
                Email = reader.GetString(2),
                IsAdmin = reader.GetBoolean(4),
                CreatedAt = reader.GetDateTime(5)
            };

            return (true, null, user, GenerateToken(user));
        }
        catch (Exception ex)
        {
            return (false, $"Login failed: {ex.Message}", null, null);
        }
    }

    public async Task<UserInfo?> GetUserByIdAsync(long id)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, username, email, is_admin, created_at, display_name, bio FROM users WHERE id = $1";
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
                Bio = reader.IsDBNull(6) ? null : reader.GetString(6)
            };
        }
        catch { return null; }
    }

    public string GenerateToken(UserInfo user)
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
}
