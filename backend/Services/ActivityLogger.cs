using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace GitXO.Api.Services;

public class ActivityLogger
{
    private readonly string _connectionString;

    public ActivityLogger(IConfiguration config)
    {
        var pg = config.GetSection("Postgres");
        _connectionString =
            $"Host={pg["Host"] ?? "localhost"};" +
            $"Port={pg["Port"] ?? "5432"};" +
            $"Database={pg["Database"] ?? "gitxo"};" +
            $"Username={pg["Username"] ?? "postgres"};" +
            $"Password={pg["Password"] ?? ""}";
    }

    /// <summary>
    /// Tests the PostgreSQL connection at startup. Non-fatal — only warns on failure.
    /// </summary>
    public async Task TestConnectionAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            Console.WriteLine("[DB] Connected to PostgreSQL successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] WARNING: Could not connect to PostgreSQL: {ex.Message}");
            Console.WriteLine("[DB] Activity logging will be disabled until connection is restored.");
        }
    }

    /// <summary>
    /// Fire-and-forget event logging. Errors are printed as warnings and never propagate.
    /// </summary>
    public void LogEvent(string eventType, string? repoName, object details, long? userId = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "INSERT INTO activity_logs (event_type, repo_name, details, user_id) VALUES ($1, $2, $3, $4)";

                cmd.Parameters.Add(new NpgsqlParameter { Value = eventType });
                cmd.Parameters.Add(new NpgsqlParameter
                {
                    Value = repoName ?? (object)DBNull.Value
                });
                cmd.Parameters.Add(new NpgsqlParameter
                {
                    Value = JsonSerializer.Serialize(details),
                    NpgsqlDbType = NpgsqlDbType.Jsonb
                });
                cmd.Parameters.Add(new NpgsqlParameter
                {
                    Value = userId.HasValue ? (object)userId.Value : DBNull.Value
                });

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[logger] Failed to log event \"{eventType}\": {ex.Message}");
            }
        });
    }
}
