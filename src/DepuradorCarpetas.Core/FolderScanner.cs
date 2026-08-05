namespace DepuradorCarpetas.Core;

/// <summary>
/// Recursively walks a folder tree detecting:
///  1) Build / dependency / cache folders (regenerable).
///  2) Old, large files worth reviewing.
/// It also associates each finding with the Git repository that contains it (if any)
/// and checks whether that repository has uncommitted changes.
/// </summary>
public sealed class FolderScanner
{
    private readonly ScanOptions _options;
    private readonly GitService _git;
    private readonly ISet<string> _enabled;
    private readonly IReadOnlyDictionary<string, FolderPattern> _catalog;

    public FolderScanner(ScanOptions options, GitService? gitService = null)
    {
        _options = options;
        _git = gitService ?? new GitService();
        _enabled = options.EnabledFolderNames ?? KnownPatterns.DefaultEnabledNames();

        var catalog = new Dictionary<string, FolderPattern>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in options.FolderPatterns)
            catalog[p.Name] = p; // on duplicate names, the last one (custom) wins
        _catalog = catalog;
    }

    /// <summary>
    /// Runs the scan. <paramref name="progress"/> receives progress messages
    /// (current path). <paramref name="cancellation"/> allows aborting.
    /// </summary>
    public ScanResult Scan(
        IProgress<string>? progress = null,
        CancellationToken cancellation = default)
    {
        var root = new DirectoryInfo(_options.RootPath);
        if (!root.Exists)
            throw new DirectoryNotFoundException($"The folder does not exist: {_options.RootPath}");

        var findings = new List<Finding>();
        var warnings = new List<string>();

        // Iterative traversal with our own stack. Each entry carries the active Git
        // repository root for its subtree, so every finding knows which repo it
        // belongs to.
        var stack = new Stack<(DirectoryInfo Dir, string? RepoRoot)>();
        stack.Push((root, GitService.IsRepoRoot(root.FullName) ? root.FullName : null));

        while (stack.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var (current, repoRoot) = stack.Pop();
            progress?.Report(current.FullName);

            DirectoryInfo[] subDirs;
            try
            {
                subDirs = current.GetDirectories();
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add($"Access denied: {current.FullName}");
                continue;
            }
            catch (Exception ex) when (ex is IOException or DirectoryNotFoundException)
            {
                warnings.Add($"Could not read: {current.FullName} ({ex.Message})");
                continue;
            }

            foreach (var dir in subDirs)
            {
                cancellation.ThrowIfCancellationRequested();

                // Skip symbolic links / junctions to avoid duplicates and cycles.
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;

                if (_options.ExcludedFolderNames.Contains(dir.Name))
                    continue;

                // A subfolder may start a new repository (nested repos).
                var childRepoRoot = GitService.IsRepoRoot(dir.FullName) ? dir.FullName : repoRoot;

                if (_catalog.TryGetValue(dir.Name, out var pattern) && _enabled.Contains(dir.Name))
                {
                    long size = TryCalculateDirectorySize(dir, warnings);
                    findings.Add(new Finding
                    {
                        Kind = FindingKind.JunkFolder,
                        Path = dir.FullName,
                        SizeBytes = size,
                        LastModifiedUtc = dir.LastWriteTimeUtc,
                        Reason = pattern.Description,
                        RepoRoot = repoRoot,
                    });
                    // Do not descend: the folder is reported as a whole block.
                    continue;
                }

                stack.Push((dir, childRepoRoot));
            }

            if (_options.ScanLargeFiles)
                CollectLargeOldFiles(current, repoRoot, findings, warnings, cancellation);
        }

        ResolveGitStatuses(findings, progress, cancellation);

        // Sort from largest to smallest to prioritize the decision.
        findings.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

        return new ScanResult { Findings = findings, Warnings = warnings };
    }

    /// <summary>Fills in the Git state of findings that belong to a repository.</summary>
    private void ResolveGitStatuses(
        List<Finding> findings,
        IProgress<string>? progress,
        CancellationToken cancellation)
    {
        var inRepo = findings.Where(f => f.RepoRoot != null).ToList();
        if (inRepo.Count == 0)
            return;

        if (!_options.DetectGitStatus)
        {
            foreach (var f in inRepo)
                f.RepoStatus = GitRepoStatus.Unknown;
            return;
        }

        var cache = new Dictionary<string, GitRepoStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in inRepo)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!cache.TryGetValue(f.RepoRoot!, out var status))
            {
                progress?.Report($"Checking repository: {f.RepoRoot}");
                status = _git.GetStatus(f.RepoRoot!);
                cache[f.RepoRoot!] = status;
            }
            f.RepoStatus = status;
        }
    }

    private void CollectLargeOldFiles(
        DirectoryInfo dir,
        string? repoRoot,
        List<Finding> findings,
        List<string> warnings,
        CancellationToken cancellation)
    {
        FileInfo[] files;
        try
        {
            files = dir.GetFiles();
        }
        catch (UnauthorizedAccessException)
        {
            warnings.Add($"Access denied to files: {dir.FullName}");
            return;
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException)
        {
            warnings.Add($"Could not read files: {dir.FullName} ({ex.Message})");
            return;
        }

        var ageThreshold = DateTime.UtcNow.AddDays(-_options.MinFileAgeDays);

        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
                if (file.Length < _options.MinLargeFileBytes)
                    continue;
                if (file.LastWriteTimeUtc > ageThreshold)
                    continue;

                findings.Add(new Finding
                {
                    Kind = FindingKind.LargeOldFile,
                    Path = file.FullName,
                    SizeBytes = file.Length,
                    LastModifiedUtc = file.LastWriteTimeUtc,
                    RepoRoot = repoRoot,
                    Reason = $"{SizeFormatter.Humanize(file.Length)} file, " +
                             $"not modified in {(int)(DateTime.UtcNow - file.LastWriteTimeUtc).TotalDays} days",
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not inspect: {file.FullName} ({ex.Message})");
            }
        }
    }

    /// <summary>Computes the total size of a folder recursively and tolerantly to errors.</summary>
    private static long TryCalculateDirectorySize(DirectoryInfo dir, List<string> warnings)
    {
        long total = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(dir);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            try
            {
                foreach (var file in current.GetFiles())
                {
                    try { total += file.Length; }
                    catch { /* inaccessible file: ignored in the total */ }
                }
                foreach (var sub in current.GetDirectories())
                {
                    if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        stack.Push(sub);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                warnings.Add($"Partial size at: {current.FullName} ({ex.Message})");
            }
        }
        return total;
    }
}
