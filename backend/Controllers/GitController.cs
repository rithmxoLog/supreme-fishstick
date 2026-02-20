using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using GitXO.Api.Services;

namespace GitXO.Api.Controllers;

/// <summary>
/// Implements the Git Smart HTTP protocol using git's built-in --stateless-rpc mode.
///
/// Supported operations:
///   git clone http://host/api/git/{repo}.git          (public repos: no auth)
///   git clone http://host/api/git/{repo}.git          (private repos: Basic auth)
///   git push  http://host/api/git/{repo}.git          (requires Basic auth: username + GitXO password)
///
/// For push the git client will prompt: Username / Password (use GitXO credentials).
/// </summary>
[ApiController]
[Route("api/git")]
public class GitController : ControllerBase
{
    private readonly RepoDbService _repoDb;
    private readonly AuthService _auth;
    private readonly ActivityLogger _logger;

    public GitController(RepoDbService repoDb, AuthService auth, ActivityLogger logger)
    {
        _repoDb = repoDb;
        _auth = auth;
        _logger = logger;
    }

    // ── GET /api/git/{name}.git/info/refs?service=git-upload-pack|git-receive-pack ──
    [HttpGet("{*path}")]
    public async Task HandleGet(string path)
    {
        var (repoName, rest) = ParseGitPath(path);
        if (repoName == null || rest != "info/refs")
        {
            Response.StatusCode = 404;
            return;
        }

        var service = Request.Query["service"].FirstOrDefault() ?? "";
        if (service != "git-upload-pack" && service != "git-receive-pack")
        {
            Response.StatusCode = 400;
            return;
        }

        var repoPath = await _repoDb.GetRepoPathAsync(repoName);
        if (repoPath == null) { Response.StatusCode = 404; return; }

        var meta = await _repoDb.GetRepoMetaAsync(repoName);

        // Private repos and receive-pack (push) both require Basic auth
        UserInfo? authUser = null;
        if (service == "git-receive-pack" || meta is { IsPublic: false })
        {
            authUser = await ParseBasicAuthAsync();
            if (authUser == null)
            {
                Response.StatusCode = 401;
                Response.Headers.WWWAuthenticate = "Basic realm=\"GitXO\"";
                return;
            }
        }

        if (service == "git-receive-pack")
        {
            if (!await _repoDb.UserCanWriteAsync(repoName, authUser!.Id))
            {
                Response.StatusCode = 403;
                return;
            }
            // Allow pushing to the currently checked-out branch on a non-bare repo
            await GitRunner.RunAsync(repoPath, "config", "receive.denyCurrentBranch", "updateInstead");
        }

        var gitCmd = service == "git-upload-pack" ? "upload-pack" : "receive-pack";

        // Pkt-line service announcement header required by the Smart HTTP spec
        var svcLine   = $"# service={service}\n";
        var svcLen    = (svcLine.Length + 4).ToString("x4");  // +4 for the 4-char length prefix itself
        var pktHeader = $"{svcLen}{svcLine}0000";

        Response.ContentType = $"application/x-{service}-advertisement";
        Response.Headers.CacheControl = "no-cache";

        var psi = BuildPsi(gitCmd, "--stateless-rpc", "--advertise-refs", repoPath);
        using var proc = Process.Start(psi)!;
        proc.StandardInput.Close();

        // Buffer the refs output (small — just a list of refs) so we can write the pkt header first
        using var ms = new MemoryStream();
        await proc.StandardOutput.BaseStream.CopyToAsync(ms);
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            Response.StatusCode = 500;
            return;
        }

        var headerBytes = Encoding.ASCII.GetBytes(pktHeader);
        await Response.Body.WriteAsync(headerBytes);
        await Response.Body.WriteAsync(ms.ToArray());
    }

    // ── POST /api/git/{name}.git/git-upload-pack  (clone / fetch) ──
    // ── POST /api/git/{name}.git/git-receive-pack (push)          ──
    [HttpPost("{*path}")]
    [RequestSizeLimit(500 * 1024 * 1024)]
    public async Task HandlePost(string path)
    {
        var (repoName, rest) = ParseGitPath(path);
        if (repoName == null || (rest != "git-upload-pack" && rest != "git-receive-pack"))
        {
            Response.StatusCode = 404;
            return;
        }

        var repoPath = await _repoDb.GetRepoPathAsync(repoName);
        if (repoPath == null) { Response.StatusCode = 404; return; }

        var meta = await _repoDb.GetRepoMetaAsync(repoName);

        UserInfo? authUser = null;
        if (rest == "git-receive-pack" || meta is { IsPublic: false })
        {
            authUser = await ParseBasicAuthAsync();
            if (authUser == null)
            {
                Response.StatusCode = 401;
                Response.Headers.WWWAuthenticate = "Basic realm=\"GitXO\"";
                return;
            }
        }

        if (rest == "git-receive-pack")
        {
            if (!await _repoDb.UserCanWriteAsync(repoName, authUser!.Id))
            {
                Response.StatusCode = 403;
                return;
            }
            await GitRunner.RunAsync(repoPath, "config", "receive.denyCurrentBranch", "updateInstead");
        }

        var gitCmd = rest == "git-upload-pack" ? "upload-pack" : "receive-pack";

        Response.ContentType = $"application/x-{rest}-result";
        Response.Headers.CacheControl = "no-cache";

        var psi = BuildPsi(gitCmd, "--stateless-rpc", repoPath);
        using var proc = Process.Start(psi)!;

        // Feed request body → git stdin, then stream git stdout → response.
        // Do these concurrently to avoid deadlock on large packs.
        var copyIn  = Task.Run(async () =>
        {
            await Request.Body.CopyToAsync(proc.StandardInput.BaseStream);
            proc.StandardInput.Close();
        });
        var copyOut = proc.StandardOutput.BaseStream.CopyToAsync(Response.Body);

        await Task.WhenAll(copyIn, copyOut);
        await proc.WaitForExitAsync();

        if (rest == "git-receive-pack" && proc.ExitCode == 0)
            _logger.LogEvent("GIT_PUSH", repoName, new { pushedBy = authUser?.Username }, authUser?.Id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses paths like "myrepo.git/info/refs" or "my.repo.git/git-upload-pack".
    /// Uses LastIndexOf(".git/") so repo names with dots work correctly.
    /// </summary>
    private static (string? RepoName, string? Service) ParseGitPath(string path)
    {
        var idx = path.LastIndexOf(".git/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return (null, null);
        var name = path[..idx];
        var rest = path[(idx + 5)..];
        return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rest)
            ? (null, null)
            : (name, rest);
    }

    /// <summary>Reads Authorization: Basic header and verifies via GitXO credentials.</summary>
    private async Task<UserInfo?> ParseBasicAuthAsync()
    {
        var header = Request.Headers.Authorization.FirstOrDefault();
        if (header == null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
            var colon   = decoded.IndexOf(':');
            if (colon < 0) return null;
            var username = decoded[..colon];
            var password = decoded[(colon + 1)..];
            var (ok, user) = await _auth.VerifyUsernamePasswordAsync(username, password);
            return ok ? user : null;
        }
        catch { return null; }
    }

    private static ProcessStartInfo BuildPsi(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }
}
