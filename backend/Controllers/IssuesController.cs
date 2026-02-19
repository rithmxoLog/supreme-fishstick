using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using GitXO.Api.Services;

namespace GitXO.Api.Controllers;

[ApiController]
[Route("api/repos/{repoName}/issues")]
public class IssuesController : ControllerBase
{
    private readonly RepoDbService _repoDb;
    private readonly ActivityLogger _logger;
    private readonly string _connectionString;

    public IssuesController(IConfiguration config, RepoDbService repoDb, ActivityLogger logger)
    {
        _repoDb = repoDb;
        _logger = logger;
        var pg = config.GetSection("Postgres");
        _connectionString =
            $"Host={pg["Host"] ?? "localhost"};" +
            $"Port={pg["Port"] ?? "5432"};" +
            $"Database={pg["Database"] ?? "gitxo"};" +
            $"Username={pg["Username"] ?? "postgres"};" +
            $"Password={pg["Password"] ?? ""}";
    }

    private long? GetUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(idStr, out var id) ? id : null;
    }

    // GET /api/repos/{repoName}/issues?status=open
    [HttpGet]
    public async Task<IActionResult> GetIssues(string repoName, [FromQuery] string? status)
    {
        var repoId = await _repoDb.GetRepoIdAsync(repoName);
        if (repoId == null)
            return NotFound(new { error = "Repository not found or not registered" });

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var whereStatus = !string.IsNullOrEmpty(status) ? " AND i.status = $2" : "";
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT i.id, i.number, i.title, i.body, i.status, i.created_at, i.updated_at,
                       u.username AS author_username, u.display_name AS author_display,
                       (SELECT COUNT(*) FROM issue_comments ic WHERE ic.issue_id = i.id) AS comment_count
                FROM issues i
                JOIN users u ON i.author_id = u.id
                WHERE i.repo_id = $1{whereStatus}
                ORDER BY i.created_at DESC";

            cmd.Parameters.Add(new NpgsqlParameter { Value = repoId.Value });
            if (!string.IsNullOrEmpty(status))
                cmd.Parameters.Add(new NpgsqlParameter { Value = status });

            var issues = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                issues.Add(new
                {
                    id = reader.GetInt64(0),
                    number = reader.GetInt32(1),
                    title = reader.GetString(2),
                    body = reader.IsDBNull(3) ? null : reader.GetString(3),
                    status = reader.GetString(4),
                    createdAt = reader.GetDateTime(5),
                    updatedAt = reader.GetDateTime(6),
                    author = new
                    {
                        username = reader.GetString(7),
                        displayName = reader.IsDBNull(8) ? null : reader.GetString(8)
                    },
                    commentCount = reader.GetInt64(9)
                });
            }

            return Ok(issues);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // GET /api/repos/{repoName}/issues/{number}
    [HttpGet("{number:int}")]
    public async Task<IActionResult> GetIssue(string repoName, int number)
    {
        var repoId = await _repoDb.GetRepoIdAsync(repoName);
        if (repoId == null)
            return NotFound(new { error = "Repository not found" });

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var issueCmd = conn.CreateCommand();
            issueCmd.CommandText = @"
                SELECT i.id, i.number, i.title, i.body, i.status, i.created_at, i.updated_at,
                       u.username, u.display_name
                FROM issues i
                JOIN users u ON i.author_id = u.id
                WHERE i.repo_id = $1 AND i.number = $2";
            issueCmd.Parameters.Add(new NpgsqlParameter { Value = repoId.Value });
            issueCmd.Parameters.Add(new NpgsqlParameter { Value = number });

            await using var reader = await issueCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return NotFound(new { error = "Issue not found" });

            var issueId = reader.GetInt64(0);
            var issue = new
            {
                id = issueId,
                number = reader.GetInt32(1),
                title = reader.GetString(2),
                body = reader.IsDBNull(3) ? null : reader.GetString(3),
                status = reader.GetString(4),
                createdAt = reader.GetDateTime(5),
                updatedAt = reader.GetDateTime(6),
                author = new
                {
                    username = reader.GetString(7),
                    displayName = reader.IsDBNull(8) ? null : reader.GetString(8)
                }
            };
            await reader.CloseAsync();

            // Get comments
            await using var commentsCmd = conn.CreateCommand();
            commentsCmd.CommandText = @"
                SELECT ic.id, ic.body, ic.created_at, u.username, u.display_name
                FROM issue_comments ic
                JOIN users u ON ic.author_id = u.id
                WHERE ic.issue_id = $1
                ORDER BY ic.created_at ASC";
            commentsCmd.Parameters.Add(new NpgsqlParameter { Value = issueId });

            var comments = new List<object>();
            await using var commentsReader = await commentsCmd.ExecuteReaderAsync();
            while (await commentsReader.ReadAsync())
            {
                comments.Add(new
                {
                    id = commentsReader.GetInt64(0),
                    body = commentsReader.GetString(1),
                    createdAt = commentsReader.GetDateTime(2),
                    author = new
                    {
                        username = commentsReader.GetString(3),
                        displayName = commentsReader.IsDBNull(4) ? null : commentsReader.GetString(4)
                    }
                });
            }

            return Ok(new { issue, comments });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // POST /api/repos/{repoName}/issues
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateIssue(string repoName, [FromBody] CreateIssueRequest body)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(body.Title))
            return BadRequest(new { error = "Title is required" });

        var repoId = await _repoDb.GetRepoIdAsync(repoName);
        if (repoId == null)
            return NotFound(new { error = "Repository not found or not registered" });

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Get next issue number
            await using var numCmd = conn.CreateCommand();
            numCmd.CommandText = "SELECT COALESCE(MAX(number), 0) + 1 FROM issues WHERE repo_id = $1";
            numCmd.Parameters.Add(new NpgsqlParameter { Value = repoId.Value });
            var nextNumber = (long)(await numCmd.ExecuteScalarAsync())!;

            await using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO issues (repo_id, number, author_id, title, body)
                VALUES ($1, $2, $3, $4, $5)
                RETURNING id, number, created_at";
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = repoId.Value });
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = (int)nextNumber });
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = userId.Value });
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = body.Title });
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = (object?)body.Body ?? DBNull.Value });

            await using var reader = await insertCmd.ExecuteReaderAsync();
            await reader.ReadAsync();

            _logger.LogEvent("ISSUE_CREATED", repoName, new { number = nextNumber, title = body.Title }, userId);
            return StatusCode(201, new
            {
                id = reader.GetInt64(0),
                number = reader.GetInt32(1),
                createdAt = reader.GetDateTime(2),
                title = body.Title
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // PATCH /api/repos/{repoName}/issues/{number}
    [HttpPatch("{number:int}")]
    [Authorize]
    public async Task<IActionResult> UpdateIssue(string repoName, int number, [FromBody] UpdateIssueRequest body)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var repoId = await _repoDb.GetRepoIdAsync(repoName);
        if (repoId == null) return NotFound(new { error = "Repository not found" });

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var sets = new List<string>();
            if (body.Status != null) sets.Add("status = $3");
            if (body.Title != null) sets.Add("title = $4");
            if (body.Body != null) sets.Add("body = $5");
            sets.Add("updated_at = NOW()");

            if (sets.Count == 1) return BadRequest(new { error = "No fields to update" });

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE issues SET {string.Join(", ", sets)} WHERE repo_id = $1 AND number = $2";
            cmd.Parameters.Add(new NpgsqlParameter { Value = repoId.Value });
            cmd.Parameters.Add(new NpgsqlParameter { Value = number });
            cmd.Parameters.Add(new NpgsqlParameter { Value = body.Status ?? (object)DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter { Value = body.Title ?? (object)DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter { Value = body.Body ?? (object)DBNull.Value });

            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0) return NotFound(new { error = "Issue not found" });

            _logger.LogEvent("ISSUE_UPDATED", repoName, new { number, status = body.Status }, userId);
            return Ok(new { message = "Issue updated" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // POST /api/repos/{repoName}/issues/{number}/comments
    [HttpPost("{number:int}/comments")]
    [Authorize]
    public async Task<IActionResult> AddComment(string repoName, int number, [FromBody] AddCommentRequest body)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(body.Body))
            return BadRequest(new { error = "Comment body is required" });

        var repoId = await _repoDb.GetRepoIdAsync(repoName);
        if (repoId == null) return NotFound(new { error = "Repository not found" });

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Get issue id
            await using var issueCmd = conn.CreateCommand();
            issueCmd.CommandText = "SELECT id FROM issues WHERE repo_id = $1 AND number = $2";
            issueCmd.Parameters.Add(new NpgsqlParameter { Value = repoId.Value });
            issueCmd.Parameters.Add(new NpgsqlParameter { Value = number });
            var issueId = await issueCmd.ExecuteScalarAsync();
            if (issueId == null) return NotFound(new { error = "Issue not found" });

            await using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO issue_comments (issue_id, author_id, body)
                VALUES ($1, $2, $3)
                RETURNING id, created_at";
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = (long)issueId });
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = userId.Value });
            insertCmd.Parameters.Add(new NpgsqlParameter { Value = body.Body });

            await using var reader = await insertCmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var newId = reader.GetInt64(0);
            var newCreatedAt = reader.GetDateTime(1);
            await reader.CloseAsync();

            // Update issue updated_at
            await using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = "UPDATE issues SET updated_at = NOW() WHERE id = $1";
            updateCmd.Parameters.Add(new NpgsqlParameter { Value = (long)issueId });
            await updateCmd.ExecuteNonQueryAsync();

            return StatusCode(201, new
            {
                id = newId,
                createdAt = newCreatedAt
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public record CreateIssueRequest(string Title, string? Body);
public record UpdateIssueRequest(string? Status, string? Title, string? Body);
public record AddCommentRequest(string Body);
