namespace DepuradorCarpetas.Core;

/// <summary>
/// Persistent user configuration, shared between the GUI and the CLI.
/// Serialized to JSON (see <see cref="ConfigStore"/>).
/// </summary>
public sealed class AppConfig
{
    /// <summary>Last analyzed folder (to reopen where the user left off).</summary>
    public string? LastRootPath { get; set; }

    /// <summary>Minimum large-file size, as text (e.g. "100MB", "1.5GB").</summary>
    public string MinSizeText { get; set; } = "100MB";

    /// <summary>Minimum age (days) for a file to be considered old.</summary>
    public int MinFileAgeDays { get; set; } = 180;

    /// <summary>Detect old, large files.</summary>
    public bool ScanLargeFiles { get; set; } = true;

    /// <summary>Check the state of Git repositories.</summary>
    public bool DetectGitStatus { get; set; } = true;

    /// <summary>Send to Recycle Bin (true) or delete permanently (false).</summary>
    public bool UseRecycleBin { get; set; } = true;

    /// <summary>
    /// Folder names enabled for detection. If null, the default-enabled ones
    /// from the catalog (built-in + custom) are used.
    /// </summary>
    public List<string>? EnabledFolderNames { get; set; }

    /// <summary>Folder patterns defined by the user (in addition to the built-in catalog).</summary>
    public List<FolderPattern> CustomPatterns { get; set; } = new();

    /// <summary>Folder names that are skipped entirely during traversal.</summary>
    public List<string> ExcludedFolderNames { get; set; } = new();

    /// <summary>Full catalog: built-in patterns combined with the custom ones.</summary>
    public IReadOnlyList<FolderPattern> AllPatterns()
    {
        var dict = new Dictionary<string, FolderPattern>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in KnownPatterns.All) dict[p.Name] = p;
        foreach (var p in CustomPatterns) dict[p.Name] = p; // the custom one wins
        return dict.Values.ToList();
    }

    /// <summary>Set of enabled names (the explicit ones, or the defaults).</summary>
    public ISet<string> ResolveEnabled()
    {
        if (EnabledFolderNames is not null)
            return new HashSet<string>(EnabledFolderNames, StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            AllPatterns().Where(p => p.EnabledByDefault).Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    public HashSet<string> ResolveExcluded()
        => new(ExcludedFolderNames, StringComparer.OrdinalIgnoreCase);
}
