using Npgsql;

namespace GitXO.Api.Services;

public class RepoDbService
{
    private readonly string _connectionString;
    private readonly string _reposDir;

    public RepoDbService(IConfiguration config)
    {
        var pg = config.GetSection("Postgres");
        _connectionString =
            $"Host={pg["Host"] ?? "localhost"};" +
            $"Port={pg["Port"] ?? "5432"};" +
            $"Database={pg["Database"] ?? "gitxo"};" +
            $"Username={pg["Username"] ?? "postgres"};" +
            $"Password={pg["Password"] ?? ""}";

        _reposDir = config["ReposDirectory"] ?? "repositories";
    }

    /// <summary>
    /// Resolves the filesystem path for a repository by name.
    /// All repositories live in a single directory regardless of visibility.
    /// Returns null if the repo cannot be found on disk.
    /// </summary>
    public async Task<string?> GetRepoPathAsync(string name)
    {
        var path = Path.Combine(_reposDir, name);
        if (Directory.Exists(path)) return path;
        return null;
    }

    /// <summary>Returns the base directory where a new repository should be created.</summary>
    public string GetTargetDir(bool isPublic = true) => _reposDir;

    public async Task<RepoMeta?> GetRepoMetaAsync(string name)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT r.id, r.owner_id, r.is_public, r.description, u.username, u.email, u.is_admin, r.default_branch
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
                OwnerIsAdmin  = reader.GetBoolean(6),
                DefaultBranch = reader.IsDBNull(7) ? "main" : reader.GetString(7),
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
    public async Task<long?> CreatePlaceholderUserAsync(string username, string email, bool isAdmin = false)
    {
        try
        {
            // Random 32-byte password encoded as base64 — unguessable and never revealed
            var randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var lockedHash  = BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(randomBytes));

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Use a safe fallback email if the meta file had none
            var safeEmail = string.IsNullOrWhiteSpace(email)
                ? $"{username}@recovery.gitxo.local"
                : email;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO users (username, email, password_hash, is_admin, created_at, updated_at)
                VALUES ($1, $2, $3, $4, NOW(), NOW())
                ON CONFLICT (username) DO NOTHING
                RETURNING id";
            cmd.Parameters.Add(new NpgsqlParameter { Value = username });
            cmd.Parameters.Add(new NpgsqlParameter { Value = safeEmail });
            cmd.Parameters.Add(new NpgsqlParameter { Value = lockedHash });
            cmd.Parameters.Add(new NpgsqlParameter { Value = isAdmin });

            var result = await cmd.ExecuteScalarAsync();
            if (result is long id) return id;

            // ON CONFLICT: username already exists (race between two concurrent recover calls).
            // Fetch the existing user's ID so the repo can still be re-linked correctly.
            await using var sel = conn.CreateCommand();
            sel.CommandText = "SELECT id FROM users WHERE username = $1";
            sel.Parameters.Add(new NpgsqlParameter { Value = username });
            var existing = await sel.ExecuteScalarAsync();
            return existing is long eid ? eid : null;
        }
        catch { return null; }
    }

    /// <summary>Returns true when at least one user row exists in the database.</summary>
    public async Task<bool> HasAnyUsersAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM users LIMIT 1)";
            var result = await cmd.ExecuteScalarAsync();
            return result is true;
        }
        // Fail-safe: if the DB is unreachable assume users exist so we never
        // accidentally bypass authentication on a running system.
        catch { return true; }
    }

    /// <summary>
    /// Updates mutable repo metadata (description and/or default branch) in the database.
    /// Null parameters are left unchanged (COALESCE semantics).
    /// </summary>
    public async Task<bool> UpdateRepoAsync(string name, string? description, string? defaultBranch)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE repositories
                SET description    = COALESCE($2, description),
                    default_branch = COALESCE($3, default_branch),
                    updated_at     = NOW()
                WHERE name = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = name });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)description   ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)defaultBranch ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
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
    public bool    OwnerIsAdmin  { get; set; }
    public string  DefaultBranch { get; set; } = "main";
}
