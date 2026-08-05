namespace Sweeft.Core;

/// <summary>One entry in a disk-usage ranking (an immediate child of the root).</summary>
public sealed record DiskUsageEntry(string Path, string Name, long SizeBytes, bool IsDirectory)
{
    public string HumanSize => SizeFormatter.Humanize(SizeBytes);
}

/// <summary>Aggregated disk-usage ranking.</summary>
public sealed class DiskUsageResult
{
    public required string Root { get; init; }
    public required IReadOnlyList<DiskUsageEntry> Entries { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public long TotalBytes => Entries.Sum(e => e.SizeBytes);
}

/// <summary>
/// Measures the size of each immediate child (folder or file) of a root and
/// ranks them largest-first — the "where did my space go?" view (like `du -sh *`).
/// Read-only: it never deletes anything.
/// </summary>
public sealed class DiskUsageScanner
{
    public DiskUsageResult Scan(string root, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        var dir = new DirectoryInfo(root);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"The folder does not exist: {root}");

        var entries = new List<DiskUsageEntry>();
        var warnings = new List<string>();

        DirectoryInfo[] subDirs;
        FileInfo[] files;
        try
        {
            subDirs = dir.GetDirectories();
            files = dir.GetFiles();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new IOException($"Could not read: {root} ({ex.Message})");
        }

        foreach (var sub in subDirs)
        {
            cancellation.ThrowIfCancellationRequested();
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            progress?.Report(sub.FullName);
            long size = DirectorySize(sub.FullName, warnings, cancellation);
            entries.Add(new DiskUsageEntry(sub.FullName, sub.Name, size, IsDirectory: true));
        }

        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
                entries.Add(new DiskUsageEntry(file.FullName, file.Name, file.Length, IsDirectory: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not inspect: {file.FullName} ({ex.Message})");
            }
        }

        entries.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return new DiskUsageResult { Root = dir.FullName, Entries = entries, Warnings = warnings };
    }

    private static long DirectorySize(string root, List<string> warnings, CancellationToken cancellation)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            DirectoryInfo dir;
            try { dir = new DirectoryInfo(stack.Pop()); }
            catch { continue; }

            try
            {
                foreach (var file in dir.GetFiles())
                {
                    try { total += file.Length; }
                    catch { /* inaccessible file: skip */ }
                }
                foreach (var sub in dir.GetDirectories())
                {
                    if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        stack.Push(sub.FullName);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                warnings.Add($"Partial size at: {dir.FullName} ({ex.Message})");
            }
        }
        return total;
    }
}
