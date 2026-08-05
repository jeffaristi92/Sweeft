namespace Sweeft.Core;

/// <summary>
/// Detects global package-manager caches (npm, NuGet, pip, Gradle, Cargo, Go, …)
/// — the caches that grow to many GB and are rarely cleaned. These live at
/// well-known per-user locations, resolved per OS and honoring common env vars.
/// </summary>
public sealed class GlobalCacheScanner
{
    public ScanResult Scan(IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        var findings = new List<Finding>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (description, path) in ResolveCaches())
        {
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(path) || !seen.Add(path) || !Directory.Exists(path))
                continue;

            progress?.Report(path);
            long size = DirectorySize(path, warnings, cancellation);
            if (size == 0)
                continue;

            findings.Add(new Finding
            {
                Kind = FindingKind.GlobalCache,
                Path = path,
                SizeBytes = size,
                LastModifiedUtc = SafeLastWrite(path),
                Reason = description,
            });
        }

        findings.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return new ScanResult { Findings = findings, Warnings = warnings };
    }

    // ---- Cache catalog (description + resolved path for the current OS) ----
    private static IEnumerable<(string Description, string Path)> ResolveCaches()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        bool win = OperatingSystem.IsWindows();
        bool mac = OperatingSystem.IsMacOS();

        string xdgCache = Env("XDG_CACHE_HOME", Path.Combine(home, ".cache"));
        string macCaches = Path.Combine(home, "Library", "Caches");
        string PlatformCache(string winPath, string macPath, string linuxPath)
            => win ? winPath : mac ? macPath : linuxPath;

        // npm
        yield return ("npm cache (Node.js)", Env("npm_config_cache",
            win ? Path.Combine(localAppData, "npm-cache") : Path.Combine(home, ".npm")));

        // Yarn (classic)
        yield return ("Yarn cache", PlatformCache(
            Path.Combine(localAppData, "Yarn", "Cache"),
            Path.Combine(macCaches, "Yarn"),
            Path.Combine(xdgCache, "yarn")));

        // pnpm store
        yield return ("pnpm store", PlatformCache(
            Path.Combine(localAppData, "pnpm", "store"),
            Path.Combine(home, "Library", "pnpm", "store"),
            Path.Combine(home, ".local", "share", "pnpm", "store")));

        // NuGet global packages
        yield return ("NuGet global packages (.NET)",
            Env("NUGET_PACKAGES", Path.Combine(home, ".nuget", "packages")));

        // pip
        yield return ("pip cache (Python)", win
            ? Path.Combine(localAppData, "pip", "Cache")
            : Env("PIP_CACHE_DIR", mac ? Path.Combine(macCaches, "pip") : Path.Combine(xdgCache, "pip")));

        // Gradle
        yield return ("Gradle caches (JVM)",
            Path.Combine(Env("GRADLE_USER_HOME", Path.Combine(home, ".gradle")), "caches"));

        // Maven
        yield return ("Maven repository (JVM)", Path.Combine(home, ".m2", "repository"));

        // Cargo (Rust)
        yield return ("Cargo registry (Rust)",
            Path.Combine(Env("CARGO_HOME", Path.Combine(home, ".cargo")), "registry"));

        // Go module cache
        yield return ("Go module cache",
            Env("GOMODCACHE", Path.Combine(Env("GOPATH", Path.Combine(home, "go")), "pkg", "mod")));

        // Homebrew (macOS / Linux)
        if (!win)
            yield return ("Homebrew cache", mac
                ? Path.Combine(macCaches, "Homebrew")
                : Path.Combine(xdgCache, "Homebrew"));
    }

    private static string Env(string name, string fallback)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static DateTime SafeLastWrite(string path)
    {
        try { return Directory.GetLastWriteTimeUtc(path); }
        catch { return DateTime.UtcNow; }
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
