using Microsoft.AspNetCore.Mvc;
using GitXO.Api.Services;

namespace GitXO.Api.Controllers;

[ApiController]
[Route("api/repos")]
public class CommitsController : ControllerBase
{
    private readonly ActivityLogger _logger;
    private readonly RepoDbService _repoDb;

    public CommitsController(ActivityLogger logger, RepoDbService repoDb)
    {
        _logger = logger;
        _repoDb = repoDb;
    }

    [HttpGet("{name}/commits")]
    public async Task<IActionResult> GetCommits(string name, [FromQuery] string? branch, [FromQuery] int limit = 50)
    {
        var repoPath = await _repoDb.GetRepoPathAsync(name);
        if (repoPath == null) return NotFound(new { error = "Repository not found" });

        try
        {
            var safeLimit = Math.Clamp(limit, 1, 500);
            var args = new List<string> { "log", $"--max-count={safeLimit}", "--format=%H|%h|%an|%ae|%ai|%s" };
            if (!string.IsNullOrEmpty(branch))
                args.Insert(1, branch);

            var (stdout, _, _) = await GitRunner.RunAsync(repoPath, [.. args]);

            var commits = stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => GitRunner.ParseLogLine(line))
                .Where(c => c != null)
                .Select(c => new
                {
                    hash = c!.Hash, shortHash = c.ShortHash, author = c.Author,
                    email = c.Email, date = c.Date, message = c.Message
                })
                .ToList();

            _logger.LogEvent("COMMITS_LISTED", name, new { branch, count = commits.Count, limit });
            return Ok(commits);
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("{name}/commits/{hash}")]
    public async Task<IActionResult> GetCommit(string name, string hash)
    {
        var repoPath = await _repoDb.GetRepoPathAsync(name);
        if (repoPath == null) return NotFound(new { error = "Repository not found" });

        try
        {
            var (infoOut, _, _) = await GitRunner.RunAsync(repoPath,
                "show", hash, "--format=%H|%h|%an|%ae|%ai|%s", "--no-patch");
            var commit = GitRunner.ParseLogLine(infoOut.Trim().Split('\n')[0].Trim());

            var (diffOut, _, _) = await GitRunner.RunAsync(repoPath,
                "show", hash, "--format=", "-p", "--diff-algorithm=minimal");

            _logger.LogEvent("COMMIT_ACCESSED", name, new { hash });
            return Ok(new
            {
                hash,
                diff = diffOut,
                commit = commit == null ? null : new
                {
                    hash = commit.Hash, shortHash = commit.ShortHash,
                    author = commit.Author, email = commit.Email, date = commit.Date, message = commit.Message
                }
            });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("{name}/diff")]
    public async Task<IActionResult> GetBranchDiff(string name, [FromQuery] string from, [FromQuery] string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "Both 'from' and 'to' query parameters are required" });

        var repoPath = await _repoDb.GetRepoPathAsync(name);
        if (repoPath == null) return NotFound(new { error = "Repository not found" });

        try
        {
            // Validate both refs exist
            var (_, fromErr, fromExit) = await GitRunner.RunAsync(repoPath, "rev-parse", "--verify", from);
            if (fromExit != 0) return BadRequest(new { error = $"Branch or ref '{from}' not found" });

            var (_, toErr, toExit) = await GitRunner.RunAsync(repoPath, "rev-parse", "--verify", to);
            if (toExit != 0) return BadRequest(new { error = $"Branch or ref '{to}' not found" });

            // Get the unified diff between the two refs
            var (diffOut, _, _) = await GitRunner.RunAsync(repoPath,
                "diff", $"{from}...{to}", "--diff-algorithm=minimal");

            // Get commits in 'to' that are not in 'from'
            var (logOut, _, _) = await GitRunner.RunAsync(repoPath,
                "log", $"{from}..{to}", "--format=%H|%h|%an|%ae|%ai|%s");

            var commits = logOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => GitRunner.ParseLogLine(line))
                .Where(c => c != null)
                .Select(c => new
                {
                    hash = c!.Hash, shortHash = c.ShortHash, author = c.Author,
                    email = c.Email, date = c.Date, message = c.Message
                })
                .ToList();

            _logger.LogEvent("BRANCH_DIFF", name, new { from, to, commitCount = commits.Count });
            return Ok(new { from, to, diff = diffOut, commits });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }
}
