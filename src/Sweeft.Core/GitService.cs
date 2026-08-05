using System.Diagnostics;

namespace Sweeft.Core;

/// <summary>
/// Queries the state of Git repositories by running the 'git' client.
/// Degrades gracefully to <see cref="GitRepoStatus.Unknown"/> if git is not
/// installed or the query fails.
/// </summary>
/// <remarks>
/// Security hardening — this tool runs 'git' inside repositories it discovers,
/// which may be attacker-controlled:
///  * The git executable is resolved to an absolute path from PATH, so a
///    malicious 'git' in the current directory cannot be picked up.
///  * Dangerous repo-local config that would run commands during 'git status'
///    (notably core.fsmonitor) is neutralized on the command line, and system
///    config is ignored, so scanning a hostile repo does not execute code.
/// </remarks>
public sealed class GitService
{
    private readonly Lazy<string?> _gitPath;

    public GitService() => _gitPath = new Lazy<string?>(ResolveGit);

    /// <summary>Whether a working 'git' client was found on the system.</summary>
    public bool IsGitAvailable => _gitPath.Value is not null;

    /// <summary>Determines whether a folder is the root of a Git repository.</summary>
    public static bool IsRepoRoot(string path)
    {
        // In normal repos ".git" is a folder; in submodules/worktrees it is a file.
        var git = System.IO.Path.Combine(path, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    /// <summary>
    /// Returns the state of the repository at <paramref name="repoRoot"/>.
    /// Empty 'git status --porcelain' = clean; any output = uncommitted changes.
    /// </summary>
    public GitRepoStatus GetStatus(string repoRoot)
    {
        var git = _gitPath.Value;
        if (git is null)
            return GitRepoStatus.Unknown;

        try
        {
            // '-c core.fsmonitor=' overrides any repo-local fsmonitor hook so an
            // untrusted repository cannot run a command during 'git status'.
            var (exit, output) = Run(git, repoRoot,
                "-c core.fsmonitor= status --porcelain", timeoutMs: 15000);
            if (exit != 0)
                return GitRepoStatus.Unknown;

            return string.IsNullOrWhiteSpace(output)
                ? GitRepoStatus.Clean
                : GitRepoStatus.Dirty;
        }
        catch
        {
            return GitRepoStatus.Unknown;
        }
    }

    /// <summary>Finds a real 'git' executable and verifies it runs.</summary>
    private static string? ResolveGit()
    {
        var path = FindGitExecutable();
        if (path is null)
            return null;
        try
        {
            var (exit, _) = Run(path, workingDir: null, "--version", timeoutMs: 5000);
            return exit == 0 ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Resolves the absolute path of 'git' by scanning PATH (never the CWD).</summary>
    private static string? FindGitExecutable()
    {
        // Only look for a real executable image (git.exe on Windows); we never run
        // a bare name, which would let CreateProcess search the current directory.
        var candidates = OperatingSystem.IsWindows() ? new[] { "git.exe" } : new[] { "git" };
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var exe in candidates)
            {
                try
                {
                    var candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                    // Malformed PATH entry (invalid characters): skip it.
                }
            }
        }
        return null;
    }

    private static (int ExitCode, string Output) Run(string gitPath, string? workingDir, string arguments, int timeoutMs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = gitPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDir != null)
            psi.WorkingDirectory = workingDir;

        // Defense in depth: ignore system-level config, never prompt for
        // credentials, and skip optional index locks.
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start 'git'.");

        // Drain both streams concurrently so a chatty stderr cannot fill its pipe
        // buffer and deadlock the process while we only read stdout.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException("The git query exceeded the time limit.");
        }

        // Ensure the async stream reads have flushed after exit.
        process.WaitForExit();
        string output = stdoutTask.GetAwaiter().GetResult();
        _ = stderrTask.GetAwaiter().GetResult();
        return (process.ExitCode, output);
    }
}
