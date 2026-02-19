using Npgsql;

namespace GitXO.Api.Services;

public class RepoDbService
{
    private readonly string _connectionString;

    public RepoDbService(IConfiguration config)
    {
        var pg = config.GetSection("Postgres");
        _connectionString =
            $"Host={pg["Host"] ?? "localhost"};" +
            $"Port={pg["Port"] ?? "5432"};" +
            $"Database={pg["Database"] ?? "gitxo"};" +
            $"Username={pg["Username"] ?? "postgres"};" +
            $"Password={pg["Password"] ?? ""}";
    }

    public async Task<RepoMeta?> GetRepoMetaAsync(string name)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT r.id, r.owner_id, r.is_public, r.description, u.username
                FROM repositories r
                JOIN users u ON r.owner_id = u.id
                WHERE r.name = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = name });

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new RepoMeta
            {
                Id = reader.GetInt64(0),
                OwnerId = reader.GetInt64(1),
                IsPublic = reader.GetBoolean(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                OwnerUsername = reader.GetString(4)
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

    // Get a repo's DB id by name
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
}

public class RepoMeta
{
    public long Id { get; set; }
    public long OwnerId { get; set; }
    public bool IsPublic { get; set; }
    public string? Description { get; set; }
    public string OwnerUsername { get; set; } = "";
}
