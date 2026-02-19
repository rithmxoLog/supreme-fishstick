using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GitXO.Api.Services;

namespace GitXO.Api.Controllers;

[ApiController]
[Route("api/repos")]
public class BranchesController : ControllerBase
{
    private readonly string _reposDir;
    private readonly ActivityLogger _logger;

    public BranchesController(IConfiguration config, ActivityLogger logger)
    {
        _reposDir = config["ReposDirectory"]!;
        _logger = logger;
    }

    // GET /api/repos/{name}/branches
    [HttpGet("{name}/branches")]
    public async Task<IActionResult> GetBranches(string name)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        try
        {
            var (current, all) = await GitRunner.GetBranchesAsync(repoPath);
            _logger.LogEvent("BRANCHES_LISTED", name, new { current, count = all.Count });
            return Ok(new { current, branches = all });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // POST /api/repos/{name}/branches
    [HttpPost("{name}/branches")]
    [Authorize]
    public async Task<IActionResult> CreateBranch(string name, [FromBody] CreateBranchRequest body)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        if (string.IsNullOrWhiteSpace(body.BranchName) ||
            !System.Text.RegularExpressions.Regex.IsMatch(body.BranchName, @"^[a-zA-Z0-9_\-\/\.]+$"))
            return BadRequest(new { error = "Invalid branch name" });

        try
        {
            if (!string.IsNullOrEmpty(body.FromBranch))
                await GitRunner.RunAsync(repoPath, "checkout", "-b", body.BranchName, body.FromBranch);
            else
                await GitRunner.RunAsync(repoPath, "checkout", "-b", body.BranchName);

            var (current, all) = await GitRunner.GetBranchesAsync(repoPath);
            _logger.LogEvent("BRANCH_CREATED", name, new { branchName = body.BranchName, fromBranch = body.FromBranch });

            return StatusCode(201, new
            {
                message = $"Branch '{body.BranchName}' created",
                current,
                branches = all
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // POST /api/repos/{name}/checkout
    [HttpPost("{name}/checkout")]
    [Authorize]
    public async Task<IActionResult> Checkout(string name, [FromBody] CheckoutRequest body)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        if (string.IsNullOrWhiteSpace(body.BranchName) ||
            !System.Text.RegularExpressions.Regex.IsMatch(body.BranchName, @"^[a-zA-Z0-9_\-\/\.]+$"))
            return BadRequest(new { error = "Invalid branch name" });

        try
        {
            var (_, exitCode) = await RunAndCheck(repoPath, "checkout", body.BranchName);
            if (exitCode != 0)
                return StatusCode(500, new { error = $"Failed to checkout branch '{body.BranchName}'" });

            _logger.LogEvent("BRANCH_SWITCHED", name, new { branchName = body.BranchName });
            return Ok(new { message = $"Switched to branch '{body.BranchName}'", current = body.BranchName });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // POST /api/repos/{name}/merge
    [HttpPost("{name}/merge")]
    [Authorize]
    public async Task<IActionResult> Merge(string name, [FromBody] MergeRequest body)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        var validBranch = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9_\-\/\.]+$");
        if (string.IsNullOrWhiteSpace(body.SourceBranch) || !validBranch.IsMatch(body.SourceBranch))
            return BadRequest(new { error = "Invalid source branch name" });
        if (string.IsNullOrWhiteSpace(body.TargetBranch) || !validBranch.IsMatch(body.TargetBranch))
            return BadRequest(new { error = "Invalid target branch name" });

        try
        {
            // Checkout target branch first
            await GitRunner.RunAsync(repoPath, "checkout", body.TargetBranch);

            var mergeMsg = body.Message ?? $"Merge branch '{body.SourceBranch}' into {body.TargetBranch}";
            var (stderr, exitCode) = await RunAndCheck(repoPath,
                "merge", body.SourceBranch, "--no-ff", "-m", mergeMsg);

            if (exitCode != 0)
            {
                if (stderr.Contains("CONFLICT") || stderr.Contains("conflict"))
                    return Conflict(new { error = "Merge conflict detected", details = stderr });
                return StatusCode(500, new { error = stderr });
            }

            _logger.LogEvent("BRANCH_MERGED", name, new
            {
                sourceBranch = body.SourceBranch,
                targetBranch = body.TargetBranch,
                message = body.Message
            });

            return Ok(new { message = $"Merged '{body.SourceBranch}' into '{body.TargetBranch}'" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // DELETE /api/repos/{name}/branches/{branch}
    [HttpDelete("{name}/branches/{branch}")]
    [Authorize]
    public async Task<IActionResult> DeleteBranch(string name, string branch)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        try
        {
            var (stderr, exitCode) = await RunAndCheck(repoPath, "branch", "-D", branch);
            if (exitCode != 0)
                return StatusCode(500, new { error = stderr });

            _logger.LogEvent("BRANCH_DELETED", name, new { branchName = branch });
            return Ok(new { message = $"Branch '{branch}' deleted" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static async Task<(string Stderr, int ExitCode)> RunAndCheck(string repoPath, params string[] args)
    {
        var (_, stderr, exitCode) = await GitRunner.RunAsync(repoPath, args);
        return (stderr, exitCode);
    }
}

public record CreateBranchRequest(string BranchName, string? FromBranch);
public record CheckoutRequest(string BranchName);
public record MergeRequest(string SourceBranch, string TargetBranch, string? Message);
