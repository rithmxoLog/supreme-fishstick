using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using GitXO.Api.Services;

namespace GitXO.Api.Controllers;

[ApiController]
[Route("api/repos")]
public class ReposController : ControllerBase
{
    private readonly ActivityLogger _logger;
    private readonly RepoDbService _repoDb;

    public ReposController(ActivityLogger logger, RepoDbService repoDb)
    {
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
            var userId = GetUserId();
            var repos = new List<object>();

            // Enumerate all repos from the single shared directory
            var allDirs = new List<string>();
            var reposDir = _repoDb.GetTargetDir();

            if (Directory.Exists(reposDir))
                allDirs.AddRange(Directory.GetDirectories(reposDir));

            foreach (var dir in allDirs)
            {
                if (!Directory.Exists(Path.Combine(dir, ".git"))) continue;

                var name = Path.GetFileName(dir);

                if (!string.IsNullOrEmpty(search) &&
                    !name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    continue;

                var meta = await _repoDb.GetRepoMetaAsync(name);

                // Private: only show to owner or collaborators
                if (meta is { IsPublic: false } && meta.OwnerId != userId)
                    continue;

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
                        isPublic = meta?.IsPublic ?? true,
                        owner = meta?.OwnerUsername
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
        var userId = GetUserId();
        var meta = await _repoDb.GetRepoMetaAsync(name);

        if (meta is { IsPublic: false } && meta.OwnerId != userId)
            return NotFound(new { error = "Repository not found" });

        var repoPath = await _repoDb.GetRepoPathAsync(name);
        if (repoPath == null)
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

            var canWrite = userId != null && await _repoDb.UserCanWriteAsync(name, userId.Value);

            return Ok(new
            {
                name,
                description = meta?.Description ?? ReadDescription(repoPath),
                currentBranch = current,
                branches = all,
                lastCommit,
                isPublic = meta?.IsPublic ?? true,
                owner = meta?.OwnerUsername,
                canWrite
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

        // Check if repo already exists in either directory
        var existingPath = await _repoDb.GetRepoPathAsync(name);
        if (existingPath != null)
            return Conflict(new { error = "Repository already exists" });

        var isPublic = body.IsPublic ?? true;
        var targetDir = _repoDb.GetTargetDir(isPublic);
        var repoPath = Path.Combine(targetDir, name);

        try
        {
            Directory.CreateDirectory(repoPath);

            await GitRunner.RunAsync(repoPath, "init");
            await GitRunner.RunAsync(repoPath, "config", "user.name", "GitXO User");
            await GitRunner.RunAsync(repoPath, "config", "user.email", "gitxo@local");
            // Allow git push to update the working tree when the target branch is checked out
            await GitRunner.RunAsync(repoPath, "config", "receive.denyCurrentBranch", "updateInstead");

            if (!string.IsNullOrWhiteSpace(body.Description))
                await System.IO.File.WriteAllTextAsync(
                    Path.Combine(repoPath, ".git", "description"), body.Description);

            var readme = $"# {name}\n\n{body.Description ?? "A GitXO repository."}\n";
            await System.IO.File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), readme);

            await GitRunner.RunAsync(repoPath, "add", "README.md");
            await GitRunner.RunAsync(repoPath, "commit", "-m", "Initial commit");

            await _repoDb.CreateRepoMetaAsync(name, userId.Value, isPublic, body.Description);

            // Write a local metadata file alongside the repo directory so ownership
            // can be recovered if the database is ever lost.
            var savedMeta   = await _repoDb.GetRepoMetaAsync(name);
            var ownerIsAdmin = User.FindFirstValue("is_admin") == "true";
            if (savedMeta != null)
                await RepoMetaWriter.WriteAsync(targetDir, name,
                    savedMeta.OwnerUsername, savedMeta.OwnerEmail, savedMeta.OwnerId,
                    savedMeta.IsPublic, savedMeta.Description, ownerIsAdmin: ownerIsAdmin);

            _logger.LogEvent("REPO_CREATED", name,
                new { description = body.Description, isPublic }, userId);
            return StatusCode(201, new { name, description = body.Description, isPublic, message = "Repository created successfully" });
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
        var userId = GetUserId();

        var repoPath = await _repoDb.GetRepoPathAsync(name);
        if (repoPath == null)
            return NotFound(new { error = "Repository not found" });

        try
        {
            var reposBaseDir = Path.GetDirectoryName(repoPath)!;
            DeleteDirectory(repoPath);
            await _repoDb.DeleteRepoMetaAsync(name);
            RepoMetaWriter.Delete(reposBaseDir, name);
            _logger.LogEvent("REPO_DELETED", name, new { }, userId);
            return Ok(new { message = $"Repository '{name}' deleted" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // PATCH /api/repos/{name}  — update description and/or defaultBranch
    // Rewrites the .meta.json file so it stays in sync with the database.
    [HttpPatch("{name}")]
    [Authorize]
    public async Task<IActionResult> UpdateRepo(string name, [FromBody] UpdateRepoRequest body)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized(new { error = "Authentication required" });

        var meta = await _repoDb.GetRepoMetaAsync(name);
        if (meta == null) return NotFound(new { error = "Repository not found" });

        var isAdminUser = User.FindFirstValue("is_admin") == "true";
        if (meta.OwnerId != userId && !isAdminUser)
            return StatusCode(403, new { error = "Only the owner or an admin can update this repository" });

        if (!await _repoDb.UpdateRepoAsync(name, body.Description, body.DefaultBranch))
            return StatusCode(500, new { error = "Failed to update repository in database" });

        // Refresh meta from DB and rewrite the meta file to prevent it going stale.
        var updated  = await _repoDb.GetRepoMetaAsync(name);
        var repoPath = await _repoDb.GetRepoPathAsync(name);
        if (updated != null && repoPath != null)
        {
            var baseDir = Path.GetDirectoryName(repoPath)!;
            await RepoMetaWriter.WriteAsync(baseDir, name,
                updated.OwnerUsername, updated.OwnerEmail, updated.OwnerId,
                updated.IsPublic, updated.Description,
                defaultBranch: updated.DefaultBranch,
                ownerIsAdmin: updated.OwnerIsAdmin);

            // Keep .git/description in sync when description changes.
            if (body.Description != null)
                await System.IO.File.WriteAllTextAsync(
                    Path.Combine(repoPath, ".git", "description"), body.Description);
        }

        _logger.LogEvent("REPO_UPDATED", name, new { body.Description, body.DefaultBranch }, userId);
        return Ok(new { name, description = updated?.Description, defaultBranch = updated?.DefaultBranch });
    }

    // POST /api/repos/recover
    //
    // Normal mode  (users exist in DB): requires admin JWT.
    // Bootstrap mode (DB fully wiped, 0 users): allows unauthenticated access so
    //   recovery can proceed without needing a login first.  Once at least one user
    //   row is present the endpoint reverts to requiring admin auth automatically.
    //
    // Scans *.meta.json files in both repo directories and re-inserts any repos
    // that exist on disk but are missing from the database.  If the original owner
    // no longer exists a placeholder user account is created (random locked password);
    // admin flag is restored from the meta file.
    [HttpPost("recover")]
    [AllowAnonymous]
    public async Task<IActionResult> RecoverFromMetaFiles()
    {
        var hasUsers    = await _repoDb.HasAnyUsersAsync();
        var isAdmin     = User.FindFirstValue("is_admin") == "true";
        var bootstrapMode = !hasUsers;

        if (hasUsers && !isAdmin)
            return StatusCode(403, new { error = "Admin access required. Provide an admin JWT in the Authorization header." });

        var reposDir = _repoDb.GetTargetDir();

        var recovered           = new List<object>();
        var skipped             = new List<string>();
        var failed              = new List<object>();
        var placeholdersCreated = new List<string>(); // usernames of newly-created placeholder accounts

        var allMeta = RepoMetaWriter.ScanAll(reposDir);

        foreach (var meta in allMeta)
        {
            // Skip if already in the database
            var existing = await _repoDb.GetRepoMetaAsync(meta.Name);
            if (existing != null) { skipped.Add(meta.Name); continue; }

            // Try to find the owner by username
            var ownerId = await _repoDb.GetUserIdByUsernameAsync(meta.OwnerUsername);

            // Owner missing from DB — create a locked placeholder account so the
            // repo can still be re-linked.  Real owner resets password to reclaim.
            if (ownerId == null)
            {
                ownerId = await _repoDb.CreatePlaceholderUserAsync(meta.OwnerUsername, meta.OwnerEmail, meta.OwnerIsAdmin);
                if (ownerId == null)
                {
                    failed.Add(new
                    {
                        repo   = meta.Name,
                        reason = $"Could not create placeholder user for '{meta.OwnerUsername}'"
                    });
                    continue;
                }
                placeholdersCreated.Add(meta.OwnerUsername);
            }

            var ok = await _repoDb.CreateRepoMetaAsync(meta.Name, ownerId.Value, meta.IsPublic, meta.Description);
            if (ok)
                recovered.Add(new { repo = meta.Name, owner = meta.OwnerUsername });
            else
                failed.Add(new { repo = meta.Name, reason = "Database insert failed" });
        }

        _logger.LogEvent("REPOS_RECOVERED", null, new
        {
            recovered           = recovered.Count,
            skipped             = skipped.Count,
            failed              = failed.Count,
            placeholdersCreated = placeholdersCreated.Count
        }, GetUserId());

        return Ok(new
        {
            recovered,
            skipped,
            failed,
            placeholdersCreated,
            bootstrapMode,
            note = placeholdersCreated.Count > 0
                ? "Placeholder accounts were created for missing users. These accounts have random locked passwords — owners must have an admin set a new password before they can log in."
                : null
        });
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            System.IO.File.SetAttributes(file, FileAttributes.Normal);
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
public record UpdateRepoRequest(string? Description, string? DefaultBranch);
