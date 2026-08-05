namespace Sweeft.Core;

/// <summary>Classification of an item found during the scan.</summary>
public enum FindingKind
{
    /// <summary>Regenerable build, dependency or cache folder.</summary>
    JunkFolder,

    /// <summary>Old, large file worth reviewing.</summary>
    LargeOldFile,
}

/// <summary>State of the Git repository that contains a finding.</summary>
public enum GitRepoStatus
{
    /// <summary>The item is not inside a Git repository.</summary>
    None,

    /// <summary>Repository with no pending changes (clean working tree).</summary>
    Clean,

    /// <summary>Repository with uncommitted changes (modifications or new files).</summary>
    Dirty,

    /// <summary>It is a repository, but its state could not be determined (git unavailable or not checked).</summary>
    Unknown,
}

/// <summary>An item (folder or file) that is a candidate for cleanup.</summary>
public sealed class Finding
{
    public required FindingKind Kind { get; init; }

    /// <summary>Full path of the item.</summary>
    public required string Path { get; init; }

    /// <summary>Total size in bytes (recursive, in the case of folders).</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Last modification date of the item.</summary>
    public required DateTime LastModifiedUtc { get; init; }

    /// <summary>Human-readable reason/description of the finding.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// For junk folders: the most recent activity (last-write) of the owning
    /// project, when stale filtering is enabled. Null otherwise.
    /// </summary>
    public DateTime? ProjectLastActivityUtc { get; init; }

    /// <summary>Days since the owning project was last active, if known.</summary>
    public int? ProjectIdleDays => ProjectLastActivityUtc is { } t
        ? (int)(DateTime.UtcNow - t).TotalDays
        : null;

    /// <summary>Root of the Git repository containing it, or null if not applicable.</summary>
    public string? RepoRoot { get; init; }

    /// <summary>State of the containing repository. Filled in after the scan.</summary>
    public GitRepoStatus RepoStatus { get; set; } = GitRepoStatus.None;

    public bool IsDirectory => Kind == FindingKind.JunkFolder;

    public string HumanSize => SizeFormatter.Humanize(SizeBytes);

    public int AgeDays => (int)(DateTime.UtcNow - LastModifiedUtc).TotalDays;
}

/// <summary>Options that control the scan behavior.</summary>
public sealed class ScanOptions
{
    /// <summary>Root folder to analyze.</summary>
    public required string RootPath { get; init; }

    /// <summary>Minimum size (bytes) to report a large file. Default 100 MB.</summary>
    public long MinLargeFileBytes { get; init; } = 100L * 1024 * 1024;

    /// <summary>Minimum age (days) for a file to be considered "old". Default 180.</summary>
    public int MinFileAgeDays { get; init; } = 180;

    /// <summary>If true, also looks for old, large files (not just folders).</summary>
    public bool ScanLargeFiles { get; init; } = true;

    /// <summary>
    /// Catalog of folder patterns to consider. Defaults to the built-in one.
    /// Allows adding user-defined patterns.
    /// </summary>
    public IReadOnlyList<FolderPattern> FolderPatterns { get; init; } = KnownPatterns.All;

    /// <summary>
    /// Folder names to detect. If null, the default-enabled patterns from
    /// <see cref="KnownPatterns"/> are used. Allows, for example, excluding ".vs".
    /// </summary>
    public ISet<string>? EnabledFolderNames { get; init; }

    /// <summary>If true, detects Git repositories and checks whether they have uncommitted changes.</summary>
    public bool DetectGitStatus { get; init; } = true;

    /// <summary>
    /// If set, a regenerable folder is only reported when its owning project has
    /// not been modified within this window (i.e. the project is "stale"). This
    /// avoids flagging projects you are actively working on.
    /// </summary>
    public TimeSpan? StaleProjectThreshold { get; init; }

    /// <summary>Folder names to skip entirely during traversal.</summary>
    public HashSet<string> ExcludedFolderNames { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Aggregated result of the scan.</summary>
public sealed class ScanResult
{
    public required IReadOnlyList<Finding> Findings { get; init; }

    /// <summary>Non-fatal errors encountered (access denied, etc.).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public long TotalReclaimableBytes => Findings.Sum(f => f.SizeBytes);

    public IEnumerable<Finding> JunkFolders => Findings.Where(f => f.Kind == FindingKind.JunkFolder);

    public IEnumerable<Finding> LargeOldFiles => Findings.Where(f => f.Kind == FindingKind.LargeOldFile);
}
