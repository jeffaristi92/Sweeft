namespace DepuradorCarpetas.Core;

/// <summary>Definition of a folder pattern that is a candidate for cleanup.</summary>
/// <param name="Name">Exact folder name (case-insensitive).</param>
/// <param name="Category">Ecosystem/grouping used when presenting to the user.</param>
/// <param name="Description">Human-readable description of what it contains.</param>
/// <param name="EnabledByDefault">Whether it is detected by default (ambiguous ones ship off).</param>
public sealed record FolderPattern(
    string Name,
    string Category,
    string Description,
    bool EnabledByDefault = true);

/// <summary>
/// Catalog of folders that typically hold regenerable build artifacts,
/// dependencies or caches and are therefore candidates for deletion.
/// </summary>
public static class KnownPatterns
{
    public static readonly IReadOnlyList<FolderPattern> All = new List<FolderPattern>
    {
        // JavaScript / Node
        new("node_modules",     "JavaScript / Node", "Node.js dependencies"),
        new(".next",            "JavaScript / Node", "Next.js build cache"),
        new(".nuxt",            "JavaScript / Node", "Nuxt.js build cache"),
        new(".angular",         "JavaScript / Node", "Angular cache"),
        new(".svelte-kit",      "JavaScript / Node", "SvelteKit cache"),
        new("bower_components", "JavaScript / Node", "Bower dependencies"),
        new(".parcel-cache",    "JavaScript / Node", "Parcel cache"),

        // .NET
        new("bin",              ".NET", ".NET build output"),
        new("obj",              ".NET", ".NET intermediate objects"),
        new(".vs",              ".NET", "Visual Studio cache"),
        new("packages",         ".NET", "NuGet packages (solution level)", EnabledByDefault: false),

        // Java / JVM
        new("target",           "Java / JVM", "Maven build output"),
        new(".gradle",          "Java / JVM", "Gradle cache"),

        // Python
        new("__pycache__",      "Python", "Python bytecode"),
        new(".pytest_cache",    "Python", "pytest cache"),
        new(".mypy_cache",      "Python", "mypy cache"),
        new(".venv",            "Python", "Python virtual environment"),
        new("venv",             "Python", "Python virtual environment"),
        new("env",              "Python", "Python virtual environment (generic name)", EnabledByDefault: false),

        // Generic build
        new("Debug",            "Generic build", "Build output (Debug)"),
        new("Release",          "Generic build", "Build output (Release)"),
        new("dist",             "Generic build", "Generated distribution"),
        new("build",            "Generic build", "Build output"),
        new("out",              "Generic build", "Build output"),

        // Other
        new(".cache",           "Other", "Generic cache"),
        new("vendor",           "Other", "Third-party dependencies (vendor)", EnabledByDefault: false),
    };

    private static readonly Dictionary<string, FolderPattern> ByName =
        All.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Names enabled by default (for console mode / when no explicit selection is given).</summary>
    public static ISet<string> DefaultEnabledNames() =>
        new HashSet<string>(
            All.Where(p => p.EnabledByDefault).Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the pattern associated with a folder name, if any.</summary>
    public static bool TryGet(string folderName, out FolderPattern pattern)
        => ByName.TryGetValue(folderName, out pattern!);
}
