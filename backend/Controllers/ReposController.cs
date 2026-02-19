using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using GitXO.Api.Services;

namespace GitXO.Api.Controllers;

[ApiController]
[Route("api/repos")]
public class ReposController : ControllerBase
{
    private readonly string _reposDir;
    private readonly ActivityLogger _logger;
    private readonly RepoDbService _repoDb;

    public ReposController(IConfiguration config, ActivityLogger logger, RepoDbService repoDb)
    {
        _reposDir = config["ReposDirectory"]!;
        _logger = logger;
        _repoDb = repoDb;
    }

    private long? GetUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(idStr, out var id) ? id : null;
    }

    // GET /api/repos?search=...&publicOnly=true
    [HttpGet]
    public async Task<IActionResult> GetRepos([FromQuery] string? search, [FromQuery] bool? publicOnly)
    {
        try
        {
            if (!Directory.Exists(_reposDir))
                return Ok(Array.Empty<object>());

            var userId = GetUserId();
            var repos = new List<object>();

            foreach (var dir in Directory.GetDirectories(_reposDir))
            {
                if (!Directory.Exists(Path.Combine(dir, ".git"))) continue;

                var name = Path.GetFileName(dir);

                // Apply search filter
                if (!string.IsNullOrEmpty(search) &&
                    !name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Get DB metadata
                var meta = await _repoDb.GetRepoMetaAsync(name);

                // If private: only show to owner
                if (meta is { IsPublic: false } && meta.OwnerId != userId)
                    continue;

                // Apply publicOnly filter
                if (publicOnly == true && meta != null && !meta.IsPublic)
                    continue;

                try
                {
                    var (current, all) = await GitRunner.GetBranchesAsync(dir);
                    var (logOut, _, _) = await GitRunner.RunAsync(dir,
                        "log", "--max-count=1", "--format=%H|%h|%an|%ae|%ai|%s");

                    var lastCommit = ParseLastCommit(logOut.Trim());
                    var stat = new DirectoryInfo(dir);

                    repos.Add(new
                    {
                        name,
                        description = meta?.Description ?? ReadDescription(dir),
                        currentBranch = current,
                        branches = all,
                        lastCommit,
                        createdAt = stat.CreationTimeUtc,
                        isPublic = meta?.IsPublic ?? true,
                        owner = meta?.OwnerUsername
                    });
                }
                catch
                {
                    repos.Add(new
                    {
                        name,
                        description = "",
                        branches = Array.Empty<string>(),
                        lastCommit = (object?)null,
                        isPublic = true,
                        owner = (string?)null
                    });
                }
            }

            _logger.LogEvent("REPOS_LISTED", null, new { count = repos.Count, search }, userId);
            return Ok(repos);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // GET /api/repos/{name}
    [HttpGet("{name}")]
    public async Task<IActionResult> GetRepo(string name)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        var userId = GetUserId();
        var meta = await _repoDb.GetRepoMetaAsync(name);

        // Private repo: only owner can access
        if (meta is { IsPublic: false } && meta.OwnerId != userId)
            return NotFound(new { error = "Repository not found" });

        try
        {
            var (current, all) = await GitRunner.GetBranchesAsync(repoPath);
            var (logOut, _, _) = await GitRunner.RunAsync(repoPath,
                "log", "--max-count=1", "--format=%H|%h|%an|%ae|%ai|%s");

            var lastCommit = ParseLastCommit(logOut.Trim());

            _logger.LogEvent("REPO_ACCESSED", name, new
            {
                currentBranch = current,
                branchCount = all.Count
            }, userId);

            return Ok(new
            {
                name,
                description = meta?.Description ?? ReadDescription(repoPath),
                currentBranch = current,
                branches = all,
                lastCommit,
                isPublic = meta?.IsPublic ?? true,
                owner = meta?.OwnerUsername
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // POST /api/repos
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateRepo([FromBody] CreateRepoRequest body)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized(new { error = "Authentication required" });

        var name = body.Name;
        if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_\-\.]+$"))
            return BadRequest(new { error = "Invalid repository name. Use alphanumeric characters, dashes, underscores, or dots." });

        var repoPath = Path.Combine(_reposDir, name);
        if (Directory.Exists(repoPath))
            return Conflict(new { error = "Repository already exists" });

        try
        {
            Directory.CreateDirectory(repoPath);

            await GitRunner.RunAsync(repoPath, "init");
            await GitRunner.RunAsync(repoPath, "config", "user.name", "GitXO User");
            await GitRunner.RunAsync(repoPath, "config", "user.email", "gitxo@local");

            if (!string.IsNullOrWhiteSpace(body.Description))
                await System.IO.File.WriteAllTextAsync(
                    Path.Combine(repoPath, ".git", "description"), body.Description);

            var readme = $"# {name}\n\n{body.Description ?? "A GitXO repository."}\n";
            await System.IO.File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), readme);

            await GitRunner.RunAsync(repoPath, "add", "README.md");
            await GitRunner.RunAsync(repoPath, "commit", "-m", "Initial commit");

            // Register in DB
            await _repoDb.CreateRepoMetaAsync(name, userId.Value, body.IsPublic ?? true, body.Description);

            _logger.LogEvent("REPO_CREATED", name, new { description = body.Description, isPublic = body.IsPublic ?? true }, userId);
            return StatusCode(201, new { name, description = body.Description, message = "Repository created successfully" });
        }
        catch (Exception ex)
        {
            if (Directory.Exists(repoPath))
                Directory.Delete(repoPath, recursive: true);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // DELETE /api/repos/{name}
    [HttpDelete("{name}")]
    [Authorize]
    public async Task<IActionResult> DeleteRepo(string name)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        var userId = GetUserId();
        var meta = await _repoDb.GetRepoMetaAsync(name);

        var isAdmin = User.FindFirstValue("is_admin") == "true";

        // Repo not in DB: only admins may delete it
        if (meta == null && !isAdmin)
            return Forbid();

        // Repo in DB: only the owner or an admin may delete it
        if (meta != null && meta.OwnerId != userId && !isAdmin)
            return Forbid();

        try
        {
            // Force-delete read-only git files on Windows
            DeleteDirectory(repoPath);
            await _repoDb.DeleteRepoMetaAsync(name);
            _logger.LogEvent("REPO_DELETED", name, new { }, userId);
            return Ok(new { message = $"Repository '{name}' deleted" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            System.IO.File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }

    private static string ReadDescription(string repoPath)
    {
        var descFile = Path.Combine(repoPath, ".git", "description");
        if (!System.IO.File.Exists(descFile)) return "";
        var content = System.IO.File.ReadAllText(descFile).Trim();
        if (!string.IsNullOrEmpty(content) && !content.StartsWith("Unnamed repository"))
            return content;
        return "";
    }

    private static object? ParseLastCommit(string logOut)
    {
        var commit = GitRunner.ParseLogLine(logOut);
        if (commit == null) return null;
        return new
        {
            hash = commit.ShortHash,
            message = commit.Message,
            date = commit.Date,
            author = commit.Author
        };
    }
}

public record CreateRepoRequest(string Name, string? Description, bool? IsPublic);
