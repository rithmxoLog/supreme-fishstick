using Microsoft.AspNetCore.Mvc;
using GitXO.Api.Services;
using Npgsql;

namespace GitXO.Api.Controllers;

[ApiController]
[Route("api/logs")]
public class LogsController : ControllerBase
{
    private readonly string _connectionString;

    public LogsController(IConfiguration config)
    {
        var pg = config.GetSection("Postgres");
        _connectionString =
            $"Host={pg["Host"] ?? "localhost"};" +
            $"Port={pg["Port"] ?? "5432"};" +
            $"Database={pg["Database"] ?? "gitxo"};" +
            $"Username={pg["Username"] ?? "postgres"};" +
            $"Password={pg["Password"] ?? ""}";
    }

    // GET /api/logs?repo=&event_type=&from=&to=&limit=100&offset=0
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? repo,
        [FromQuery] string? event_type,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        try
        {
            var safeLimit = Math.Clamp(limit, 1, 500);
            var safeOffset = Math.Max(offset, 0);

            var conditions = new List<string>();
            var paramValues = new List<object?>();

            if (!string.IsNullOrEmpty(repo))
            {
                paramValues.Add(repo);
                conditions.Add($"repo_name = ${paramValues.Count}");
            }
            if (!string.IsNullOrEmpty(event_type))
            {
                paramValues.Add(event_type);
                conditions.Add($"event_type = ${paramValues.Count}");
            }
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fromDate))
            {
                paramValues.Add(fromDate.ToUniversalTime());
                conditions.Add($"created_at >= ${paramValues.Count}");
            }
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var toDate))
            {
                toDate = toDate.AddDays(1).ToUniversalTime();
                paramValues.Add(toDate);
                conditions.Add($"created_at < ${paramValues.Count}");
            }

            var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Count query
            int total;
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*)::int AS total FROM activity_logs {where}";
                for (var i = 0; i < paramValues.Count; i++)
                    cmd.Parameters.Add(new NpgsqlParameter { Value = paramValues[i] ?? DBNull.Value });
                total = (int)(await cmd.ExecuteScalarAsync() ?? 0);
            }

            // Data query
            var logs = new List<object>();
            {
                var dataParams = new List<object?>(paramValues) { safeLimit, safeOffset };
                await using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    $@"SELECT id, event_type, repo_name, details, created_at
                       FROM activity_logs
                       {where}
                       ORDER BY created_at DESC
                       LIMIT ${dataParams.Count - 1} OFFSET ${dataParams.Count}";

                for (var i = 0; i < dataParams.Count; i++)
                    cmd.Parameters.Add(new NpgsqlParameter { Value = dataParams[i] ?? DBNull.Value });

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    logs.Add(new
                    {
                        id = reader.GetInt64(0),
                        event_type = reader.GetString(1),
                        repo_name = reader.IsDBNull(2) ? null : reader.GetString(2),
                        details = reader.IsDBNull(3) ? null : reader.GetString(3),
                        created_at = reader.GetDateTime(4)
                    });
                }
            }

            return Ok(new { total, limit = safeLimit, offset = safeOffset, logs });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[logs route] Error: {ex.Message}");
            return StatusCode(500, new { error = "Failed to fetch activity logs" });
        }
    }

    // GET /api/logs/event-types
    [HttpGet("event-types")]
    public async Task<IActionResult> GetEventTypes()
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT event_type FROM activity_logs ORDER BY event_type ASC";

            var types = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                types.Add(reader.GetString(0));

            return Ok(types);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[logs route] event-types error: {ex.Message}");
            return StatusCode(500, new { error = "Failed to fetch event types" });
        }
    }
}
