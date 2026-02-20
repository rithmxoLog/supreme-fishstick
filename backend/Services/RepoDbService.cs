using Npgsql;

namespace GitXO.Api.Services;

public class RepoDbService
{
    private readonly string _connectionString;
    private readonly string _publicReposDir;
    private readonly string _privateReposDir;

    public RepoDbService(IConfiguration config)
    {
        var pg = config.GetSection("Postgres");
        _connectionString =
            $"Host={pg["Host"] ?? "localhost"};" +
            $"Port={pg["Port"] ?? "5432"};" +
            $"Database={pg["Database"] ?? "gitxo"};" +
            $"Username={pg["Username"] ?? "postgres"};" +
            $"Password={pg["Password"] ?? ""}";

        _publicReposDir  = config["ReposDirectory"]        ?? "repositories";
        _privateReposDir = config["PrivateReposDirectory"] ?? "repositories-private";
    }

    /// <summary>
    /// Resolves the filesystem path for a repository by name.
    /// Checks the database for is_public, then searches the appropriate directory.
    /// Falls back to checking both directories for legacy/migrated repos.
    /// Returns null if the repo cannot be found on disk.
    /// </summary>
    public async Task<string?> GetRepoPathAsync(string name)
    {
        var meta = await GetRepoMetaAsync(name);
        if (meta != null)
        {
            // Check the correct directory first
            var primary = Path.Combine(meta.IsPublic ? _publicReposDir : _privateReposDir, name);
            if (Directory.Exists(primary)) return primary;

            // Fallback: repo may exist in old location (e.g. before split was introduced)
            var fallback = Path.Combine(meta.IsPublic ? _privateReposDir : _publicReposDir, name);
            if (Directory.Exists(fallback)) return fallback;
        }
        else
        {
            // Legacy / unregistered repos live in the public directory
            var legacy = Path.Combine(_publicReposDir, name);
            if (Directory.Exists(legacy)) return legacy;
        }

        return null;
    }

    /// <summary>Returns the base directory where a new repository should be created.</summary>
    public string GetTargetDir(bool isPublic) =>
        isPublic ? _publicReposDir : _privateReposDir;

    public async Task<RepoMeta?> GetRepoMetaAsync(string name)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT r.id, r.owner_id, r.is_public, r.description, u.username, u.email
                FROM repositories r
                JOIN users u ON r.owner_id = u.id
                WHERE r.name = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = name });

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new RepoMeta
            {
                Id            = reader.GetInt64(0),
                OwnerId       = reader.GetInt64(1),
                IsPublic      = reader.GetBoolean(2),
                Description   = reader.IsDBNull(3) ? null : reader.GetString(3),
                OwnerUsername = reader.GetString(4),
                OwnerEmail    = reader.GetString(5),
            };
        }
        catch { return null; }
    }

    public async Task<bool> CreateRepoMetaAsync(string name, long ownerId, bool isPublic, string? description)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO repositories (name, owner_id, is_public, description)
                VALUES ($1, $2, $3, $4)
                ON CONFLICT (name) DO NOTHING";
            cmd.Parameters.Add(new NpgsqlParameter { Value = name });
            cmd.Parameters.Add(new NpgsqlParameter { Value = ownerId });
            cmd.Parameters.Add(new NpgsqlParameter { Value = isPublic });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)description ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteRepoMetaAsync(string name)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM repositories WHERE name = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = name });
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UserCanWriteAsync(string repoName, long userId)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 1 FROM repositories r
                LEFT JOIN repo_collaborators rc ON rc.repo_id = r.id AND rc.user_id = $2
                    AND rc.role IN ('write', 'admin')
                WHERE r.name = $1
                  AND (r.owner_id = $2 OR rc.user_id IS NOT NULL)";
            cmd.Parameters.Add(new NpgsqlParameter { Value = repoName });
            cmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }
        catch { return false; }
    }

    public async Task<bool> UserCanReadAsync(string repoName, long userId)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 1 FROM repositories r
                LEFT JOIN repo_collaborators rc ON rc.repo_id = r.id AND rc.user_id = $2
                WHERE r.name = $1
                  AND (r.owner_id = $2 OR rc.user_id IS NOT NULL)";
            cmd.Parameters.Add(new NpgsqlParameter { Value = repoName });
            cmd.Parameters.Add(new NpgsqlParameter { Value = userId });
            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }
        catch { return false; }
    }

    public async Task<long?> GetRepoIdAsync(string name)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM repositories WHERE name = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = name });
            var result = await cmd.ExecuteScalarAsync();
            return result is long id ? id : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Looks up a user's numeric ID by their username.
    /// Used during metadata recovery to re-link repos to their owners.
    /// </summary>
    public async Task<long?> GetUserIdByUsernameAsync(string username)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM users WHERE username = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = username });
            var result = await cmd.ExecuteScalarAsync();
            return result is long id ? id : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Creates a placeholder user account during full recovery (DB + users wiped).
    /// The account uses a random unguessable password — the real owner must reset it
    /// via the admin panel or a future password-reset flow.
    /// Returns the new user's ID, or null on failure.
    /// </summary>
    public async Task<long?> CreatePlaceholderUserAsync(string username, string email)
    {
        try
        {
            // Random 32-byte password encoded as base64 — unguessable and never revealed
            var randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var lockedHash  = BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(randomBytes));

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            // Use a safe fallback email if the meta file had none
            var safeEmail = string.IsNullOrWhiteSpace(email)
                ? $"{username}@recovery.gitxo.local"
                : email;

            cmd.CommandText = @"
                INSERT INTO users (username, email, password_hash, created_at, updated_at)
                VALUES ($1, $2, $3, NOW(), NOW())
                ON CONFLICT (username) DO NOTHING
                RETURNING id";
            cmd.Parameters.Add(new NpgsqlParameter { Value = username });
            cmd.Parameters.Add(new NpgsqlParameter { Value = safeEmail });
            cmd.Parameters.Add(new NpgsqlParameter { Value = lockedHash });

            var result = await cmd.ExecuteScalarAsync();
            return result is long id ? id : null;
        }
        catch { return null; }
    }
}

public class RepoMeta
{
    public long    Id            { get; set; }
    public long    OwnerId       { get; set; }
    public bool    IsPublic      { get; set; }
    public string? Description   { get; set; }
    public string  OwnerUsername { get; set; } = "";
    public string  OwnerEmail    { get; set; } = "";
}
