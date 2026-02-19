using Microsoft.AspNetCore.Mvc;
using GitXO.Api.Services;

namespace GitXO.Api.Controllers;

[ApiController]
[Route("api/repos")]
public class CommitsController : ControllerBase
{
    private readonly string _reposDir;
    private readonly ActivityLogger _logger;

    public CommitsController(IConfiguration config, ActivityLogger logger)
    {
        _reposDir = config["ReposDirectory"]!;
        _logger = logger;
    }

    // GET /api/repos/{name}/commits?branch=...&limit=50
    [HttpGet("{name}/commits")]
    public async Task<IActionResult> GetCommits(string name, [FromQuery] string? branch, [FromQuery] int limit = 50)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        try
        {
            var safeLimit = Math.Clamp(limit, 1, 500);
            var args = new List<string> { "log", $"--max-count={safeLimit}", "--format=%H|%h|%an|%ae|%ai|%s" };
            if (!string.IsNullOrEmpty(branch))
                args.Insert(1, branch);  // git log <branch> --max-count=...

            var (stdout, _, _) = await GitRunner.RunAsync(repoPath, [.. args]);

            var commits = stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => GitRunner.ParseLogLine(line))
                .Where(c => c != null)
                .Select(c => new
                {
                    hash = c!.Hash,
                    shortHash = c.ShortHash,
                    author = c.Author,
                    email = c.Email,
                    date = c.Date,
                    message = c.Message
                })
                .ToList();

            _logger.LogEvent("COMMITS_LISTED", name, new { branch, count = commits.Count, limit });
            return Ok(commits);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // GET /api/repos/{name}/commits/{hash}
    [HttpGet("{name}/commits/{hash}")]
    public async Task<IActionResult> GetCommit(string name, string hash)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        try
        {
            // Get structured commit info
            var (infoOut, _, _) = await GitRunner.RunAsync(repoPath,
                "show", hash, "--format=%H|%h|%an|%ae|%ai|%s", "--no-patch");
            var commit = GitRunner.ParseLogLine(infoOut.Trim().Split('\n')[0].Trim());

            // Get full unified diff (suitable for diff2html)
            var (diffOut, _, _) = await GitRunner.RunAsync(repoPath,
                "show", hash, "--format=", "-p", "--diff-algorithm=minimal");

            _logger.LogEvent("COMMIT_ACCESSED", name, new { hash });
            return Ok(new
            {
                hash,
                diff = diffOut,
                commit = commit == null ? null : new
                {
                    hash = commit.Hash,
                    shortHash = commit.ShortHash,
                    author = commit.Author,
                    email = commit.Email,
                    date = commit.Date,
                    message = commit.Message
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
