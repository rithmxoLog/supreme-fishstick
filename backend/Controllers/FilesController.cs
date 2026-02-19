using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using GitXO.Api.Services;

namespace GitXO.Api.Controllers;

[ApiController]
[Route("api/repos")]
public class FilesController : ControllerBase
{
    private readonly string _reposDir;
    private readonly ActivityLogger _logger;

    public FilesController(IConfiguration config, ActivityLogger logger)
    {
        _reposDir = config["ReposDirectory"]!;
        _logger = logger;
    }

    private long? GetUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(idStr, out var id) ? id : null;
    }

    // GET /api/repos/{name}/files?path=subdir
    [HttpGet("{name}/files")]
    public async Task<IActionResult> ListFiles(string name, [FromQuery] string? path)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        try
        {
            var targetPath = string.IsNullOrEmpty(path)
                ? repoPath
                : SafeJoin(repoPath, path);

            if (!Directory.Exists(targetPath))
                return NotFound(new { error = "Path not found" });

            var entries = Directory.GetFileSystemEntries(targetPath)
                .Select(e => new { entry = e, name = System.IO.Path.GetFileName(e) })
                .Where(x => x.name != ".git")
                .ToList();

            var fileTasks = entries.Select(async x =>
            {
                var isDir = Directory.Exists(x.entry);
                var relPath = System.IO.Path.GetRelativePath(repoPath, x.entry).Replace('\\', '/');

                object? lastCommit = null;
                try
                {
                    var (logOut, _, _) = await GitRunner.RunAsync(repoPath,
                        "log", "--max-count=1", "--format=%H|%h|%an|%ae|%ai|%s", "--", relPath);
                    var commit = GitRunner.ParseLogLine(logOut.Trim());
                    if (commit != null)
                        lastCommit = new { hash = commit.ShortHash, message = commit.Message, date = commit.Date };
                }
                catch { }

                long? size = null;
                if (!isDir)
                {
                    try { size = new FileInfo(x.entry).Length; } catch { }
                }

                return new
                {
                    name = x.name,
                    path = relPath,
                    type = isDir ? "directory" : "file",
                    size,
                    lastCommit
                };
            });

            var files = (await Task.WhenAll(fileTasks))
                .OrderBy(f => f.type == "directory" ? 0 : 1)
                .ThenBy(f => f.name)
                .ToList();

            _logger.LogEvent("FILES_LISTED", name, new { path = path ?? "/", fileCount = files.Count });
            return Ok(new { path = path ?? "", files });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // GET /api/repos/{name}/file?path=...
    [HttpGet("{name}/file")]
    public IActionResult GetFile(string name, [FromQuery] string? path)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        if (string.IsNullOrEmpty(path))
            return BadRequest(new { error = "File path required" });

        try
        {
            var fullPath = SafeJoin(repoPath, path);
            if (!System.IO.File.Exists(fullPath))
                return NotFound(new { error = "File not found" });

            var bytes = System.IO.File.ReadAllBytes(fullPath);
            if (IsBinaryFile(bytes))
            {
                _logger.LogEvent("FILE_ACCESSED", name, new { filePath = path, isBinary = true, size = bytes.Length });
                return Ok(new { path, content = (string?)null, isBinary = true, size = bytes.Length });
            }

            var content = System.Text.Encoding.UTF8.GetString(bytes);
            _logger.LogEvent("FILE_ACCESSED", name, new { filePath = path, isBinary = false, size = bytes.Length });
            return Ok(new { path, content, isBinary = false, size = bytes.Length });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // POST /api/repos/{name}/file
    [HttpPost("{name}/file")]
    [Authorize]
    public async Task<IActionResult> SaveFile(string name, [FromBody] SaveFileRequest body)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        if (string.IsNullOrEmpty(body.FilePath)) return BadRequest(new { error = "filePath required" });
        if (body.Content == null) return BadRequest(new { error = "content required" });
        if (string.IsNullOrEmpty(body.Message)) return BadRequest(new { error = "commit message required" });

        try
        {
            if (!string.IsNullOrEmpty(body.Branch))
            {
                var (_, all) = await GitRunner.GetBranchesAsync(repoPath);
                if (all.Contains(body.Branch))
                    await GitRunner.RunAsync(repoPath, "checkout", body.Branch);
                else
                    await GitRunner.RunAsync(repoPath, "checkout", "-b", body.Branch);
            }

            var targetPath = SafeJoin(repoPath, body.FilePath);
            var targetDir = System.IO.Path.GetDirectoryName(targetPath)!;
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            var isNew = !System.IO.File.Exists(targetPath);
            await System.IO.File.WriteAllTextAsync(targetPath, body.Content, System.Text.Encoding.UTF8);

            var relPath = System.IO.Path.GetRelativePath(repoPath, targetPath).Replace('\\', '/');
            await GitRunner.RunAsync(repoPath, "add", relPath);
            await GitRunner.RunAsync(repoPath, "commit", "-m", body.Message, "--", relPath);

            var (logOut, _, _) = await GitRunner.RunAsync(repoPath,
                "log", "--max-count=1", "--format=%H|%h|%an|%ae|%ai|%s");
            var commit = GitRunner.ParseLogLine(logOut.Trim());

            _logger.LogEvent(isNew ? "FILE_CREATED" : "FILE_UPDATED", name, new
            {
                filePath = relPath,
                commitHash = commit?.ShortHash,
                commitMessage = body.Message,
                branch = body.Branch
            });

            return Ok(new
            {
                message = "File saved and committed",
                commit = commit == null ? null : new { hash = commit.ShortHash, message = commit.Message, date = commit.Date },
                file = relPath
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // POST /api/repos/{name}/push  (multipart file upload)
    [HttpPost("{name}/push")]
    [Authorize]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<IActionResult> Push(string name)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        var form = Request.Form;
        var message = form["message"].FirstOrDefault();
        var branch = form["branch"].FirstOrDefault();
        var authorName = form["authorName"].FirstOrDefault();
        var authorEmail = form["authorEmail"].FirstOrDefault();

        if (string.IsNullOrEmpty(message))
            return BadRequest(new { error = "Commit message required" });

        try
        {
            if (!string.IsNullOrEmpty(branch))
            {
                var (_, all) = await GitRunner.GetBranchesAsync(repoPath);
                if (all.Contains(branch))
                    await GitRunner.RunAsync(repoPath, "checkout", branch);
                else
                    await GitRunner.RunAsync(repoPath, "checkout", "-b", branch);
            }

            var filePaths = new List<string>();

            // Handle uploaded files
            foreach (var file in Request.Form.Files)
            {
                var relativePath = file.FileName;
                var targetPath = SafeJoin(repoPath, relativePath);
                var targetDir = System.IO.Path.GetDirectoryName(targetPath)!;
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                await using var stream = System.IO.File.Create(targetPath);
                await file.CopyToAsync(stream);
                filePaths.Add(System.IO.Path.GetRelativePath(repoPath, targetPath).Replace('\\', '/'));
            }

            // Handle JSON body file (text content via form fields)
            var fileContent = form["fileContent"].FirstOrDefault();
            var filePath = form["filePath"].FirstOrDefault();
            if (fileContent != null && !string.IsNullOrEmpty(filePath))
            {
                var targetPath = SafeJoin(repoPath, filePath);
                var targetDir = System.IO.Path.GetDirectoryName(targetPath)!;
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                await System.IO.File.WriteAllTextAsync(targetPath, fileContent, System.Text.Encoding.UTF8);
                filePaths.Add(System.IO.Path.GetRelativePath(repoPath, targetPath).Replace('\\', '/'));
            }

            if (filePaths.Count == 0)
                return BadRequest(new { error = "No files provided" });

            await GitRunner.RunAsync(repoPath, ["add", .. filePaths]);

            var commitArgs = new List<string> { "commit", "-m", message };
            if (!string.IsNullOrEmpty(authorName))
                commitArgs.AddRange(["--author", $"{authorName} <{authorEmail ?? "gitxo@local"}>"]);
            commitArgs.AddRange(["--", .. filePaths]);
            await GitRunner.RunAsync(repoPath, [.. commitArgs]);

            var (logOut, _, _) = await GitRunner.RunAsync(repoPath,
                "log", "--max-count=1", "--format=%H|%h|%an|%ae|%ai|%s");
            var commit = GitRunner.ParseLogLine(logOut.Trim());

            _logger.LogEvent("FILES_PUSHED", name, new
            {
                fileCount = filePaths.Count,
                files = filePaths,
                commitHash = commit?.ShortHash,
                commitMessage = message,
                branch
            });

            return Ok(new
            {
                message = "Files pushed successfully",
                commit = commit == null ? null : new { hash = commit.ShortHash, message = commit.Message, date = commit.Date },
                files = filePaths
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // DELETE /api/repos/{name}/file
    [HttpDelete("{name}/file")]
    [Authorize]
    public async Task<IActionResult> DeleteFile(string name, [FromBody] DeleteFileRequest body)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        if (string.IsNullOrEmpty(body.FilePath))
            return BadRequest(new { error = "filePath required" });

        try
        {
            var fullPath = SafeJoin(repoPath, body.FilePath);
            if (!System.IO.File.Exists(fullPath))
                return NotFound(new { error = "File not found" });

            var relPath = System.IO.Path.GetRelativePath(repoPath, fullPath).Replace('\\', '/');
            System.IO.File.Delete(fullPath);
            await GitRunner.RunAsync(repoPath, "rm", relPath);
            var commitMsg = body.Message ?? $"Delete {relPath}";
            await GitRunner.RunAsync(repoPath, "commit", "-m", commitMsg, "--", relPath);

            _logger.LogEvent("FILE_DELETED", name, new { filePath = relPath, commitMessage = commitMsg });
            return Ok(new { message = $"File '{relPath}' deleted and committed" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // GET /api/repos/{name}/download/file?path=...
    [HttpGet("{name}/download/file")]
    public IActionResult DownloadFile(string name, [FromQuery] string? path)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
            return NotFound(new { error = "Repository not found" });

        if (string.IsNullOrEmpty(path))
            return BadRequest(new { error = "File path required" });

        try
        {
            var fullPath = SafeJoin(repoPath, path);
            if (!System.IO.File.Exists(fullPath))
                return NotFound(new { error = "File not found" });

            var fileName = System.IO.Path.GetFileName(fullPath);
            _logger.LogEvent("FILE_DOWNLOADED", name, new { filePath = path });

            var bytes = System.IO.File.ReadAllBytes(fullPath);
            return File(bytes, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // GET /api/repos/{name}/download?branch=...
    [HttpGet("{name}/download")]
    public async Task DownloadRepo(string name, [FromQuery] string? branch)
    {
        var repoPath = Path.Combine(_reposDir, name);
        if (!Directory.Exists(repoPath))
        {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(new { error = "Repository not found" });
            return;
        }

        var (current, all) = await GitRunner.GetBranchesAsync(repoPath);
        var targetBranch = branch ?? current;

        if (!all.Contains(targetBranch))
        {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(new { error = $"Branch '{targetBranch}' not found" });
            return;
        }

        var zipName = $"{name}-{targetBranch}.zip";
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{zipName}\"";

        using var proc = GitRunner.StartStreaming(repoPath, "archive", "--format=zip", targetBranch);
        await proc.StandardOutput.BaseStream.CopyToAsync(Response.Body);
        await proc.WaitForExitAsync();

        if (proc.ExitCode == 0)
            _logger.LogEvent("REPO_DOWNLOADED", name, new { branch = targetBranch });
    }

    private static string SafeJoin(string basePath, string userPath)
    {
        var resolved = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(basePath, userPath.TrimStart('/', '\\')));
        if (!resolved.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal detected");
        return resolved;
    }

    private static bool IsBinaryFile(byte[] bytes)
    {
        var limit = Math.Min(bytes.Length, 512);
        for (var i = 0; i < limit; i++)
        {
            var b = bytes[i];
            if (b == 0) return true;
            if (b < 8 || (b > 13 && b < 32 && b != 27)) return true;
        }
        return false;
    }
}

public record SaveFileRequest(string FilePath, string? Content, string Message, string? Branch);
public record DeleteFileRequest(string FilePath, string? Message);
