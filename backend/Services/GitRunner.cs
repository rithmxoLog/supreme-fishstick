using System.Diagnostics;
using System.Text;

namespace GitXO.Api.Services;

public static class GitRunner
{
    public static async Task<(string Stdout, string Stderr, int ExitCode)> RunAsync(
        string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)!;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (stdout, stderr, proc.ExitCode);
    }

    /// <summary>
    /// Starts a git process and returns it for streaming stdout (e.g. git archive).
    /// Caller is responsible for disposing the process.
    /// </summary>
    public static Process StartStreaming(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return Process.Start(psi)!;
    }

    public static async Task<(string Current, List<string> All)> GetBranchesAsync(string repoPath)
    {
        var (stdout, _, exitCode) = await RunAsync(repoPath, "branch");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return ("main", ["main"]);

        var current = "main";
        var all = new List<string>();

        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var isCurrent = trimmed.StartsWith('*');
            var name = trimmed.TrimStart('*', ' ');
            all.Add(name);
            if (isCurrent) current = name;
        }

        return (current, all);
    }

    /// <summary>
    /// Parses a single log line formatted as %H|%h|%an|%ae|%ai|%s
    /// </summary>
    public static CommitInfo? ParseLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var parts = line.Split('|');
        if (parts.Length < 6) return null;
        return new CommitInfo
        {
            Hash = parts[0],
            ShortHash = parts[1],
            Author = parts[2],
            Email = parts[3],
            Date = parts[4],
            Message = string.Join("|", parts.Skip(5))
        };
    }
}

public record CommitInfo
{
    public string Hash { get; init; } = "";
    public string ShortHash { get; init; } = "";
    public string Author { get; init; } = "";
    public string Email { get; init; } = "";
    public string Date { get; init; } = "";
    public string Message { get; init; } = "";
}
