using System.Reflection;
using Sweeft.Core;

namespace Sweeft.ConsoleApp;

/// <summary>
/// Command-line options. They are seeded from the persistent configuration
/// (shared with the GUI) and then overridden by the flags.
/// </summary>
internal sealed class CliOptions
{
    // --- Special actions ---
    public bool ShowHelp { get; private set; }
    public bool ShowVersion { get; private set; }
    public bool ListTypes { get; private set; }
    public bool JsonOutput { get; private set; }
    public bool ReportOnly { get; private set; }
    public bool AssumeYes { get; private set; }
    public bool Force { get; private set; }
    public bool SaveConfig { get; private set; }
    public bool NoConfig { get; private set; }
    public string? ConfigPath { get; private set; }

    // --- Scan parameters (seeded from config) ---
    public string RootPath { get; private set; } = Directory.GetCurrentDirectory();
    public long MinLargeFileBytes { get; private set; } = 100L * 1024 * 1024;
    public string MinSizeText { get; private set; } = "100MB";
    public int MinFileAgeDays { get; private set; } = 180;
    public bool ScanLargeFiles { get; private set; } = true;
    public bool DetectGit { get; private set; } = true;
    public bool PermanentDelete { get; private set; }

    /// <summary>Explicit types (--types). If null, the config ones are used.</summary>
    public HashSet<string>? ExplicitTypes { get; private set; }

    public List<FolderPattern> CustomPatterns { get; } = new();
    public HashSet<string> ExcludedFolderNames { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pre-scans just to locate --config / --no-config before loading the config.</summary>
    public static (string? ConfigPath, bool NoConfig) PeekConfig(string[] args)
    {
        string? path = null;
        bool no = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--config" && i + 1 < args.Length) path = args[i + 1];
            else if (args[i] is "--no-config") no = true;
        }
        return (path, no);
    }

    public static CliOptions Parse(string[] args, AppConfig config)
    {
        var o = new CliOptions
        {
            RootPath = config.LastRootPath is { Length: > 0 } lp ? lp : Directory.GetCurrentDirectory(),
            MinSizeText = config.MinSizeText,
            MinLargeFileBytes = SafeParseSize(config.MinSizeText, 100L * 1024 * 1024),
            MinFileAgeDays = config.MinFileAgeDays,
            ScanLargeFiles = config.ScanLargeFiles,
            DetectGit = config.DetectGitStatus,
            PermanentDelete = !config.UseRecycleBin,
            ExcludedFolderNames = config.ResolveExcluded(),
        };

        bool pathSet = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    o.ShowHelp = true;
                    break;
                case "-v" or "-V" or "--version":
                    o.ShowVersion = true;
                    break;
                case "--list-types":
                    o.ListTypes = true;
                    break;
                case "--json":
                    o.JsonOutput = true;
                    break;

                case "--path" or "-p":
                    o.RootPath = RequireValue(args, ref i, arg);
                    pathSet = true;
                    break;

                case "--min-size" or "-s":
                    o.MinSizeText = RequireValue(args, ref i, arg);
                    o.MinLargeFileBytes = SizeFormatter.ParseSize(o.MinSizeText);
                    break;

                case "--min-age" or "-a":
                    o.MinFileAgeDays = int.Parse(RequireValue(args, ref i, arg));
                    break;

                case "--only-folders":
                    o.ScanLargeFiles = false;
                    break;
                case "--with-files":
                    o.ScanLargeFiles = true;
                    break;

                case "--types" or "-t":
                    o.ExplicitTypes = new HashSet<string>(
                        SplitList(RequireValue(args, ref i, arg)),
                        StringComparer.OrdinalIgnoreCase);
                    break;

                case "--exclude" or "-x":
                    foreach (var name in SplitList(RequireValue(args, ref i, arg)))
                        o.ExcludedFolderNames.Add(name);
                    break;

                case "--custom":
                    o.CustomPatterns.Add(ParseCustom(RequireValue(args, ref i, arg)));
                    break;

                case "--no-git":
                    o.DetectGit = false;
                    break;
                case "--git":
                    o.DetectGit = true;
                    break;

                case "--report-only":
                    o.ReportOnly = true;
                    break;
                case "--yes" or "-y":
                    o.AssumeYes = true;
                    break;
                case "--force":
                    o.Force = true;
                    break;
                case "--permanent":
                    o.PermanentDelete = true;
                    break;
                case "--recycle":
                    o.PermanentDelete = false;
                    break;

                case "--save-config":
                    o.SaveConfig = true;
                    break;
                case "--no-config":
                    o.NoConfig = true;
                    break;
                case "--config":
                    o.ConfigPath = RequireValue(args, ref i, arg);
                    break;

                default:
                    if (arg.StartsWith('-'))
                        throw new ArgumentException($"Unknown option: {arg}");
                    if (!pathSet)
                    {
                        o.RootPath = arg;
                        pathSet = true;
                    }
                    else
                    {
                        throw new ArgumentException($"Unexpected argument: {arg}");
                    }
                    break;
            }
        }

        o.RootPath = Path.GetFullPath(o.RootPath);
        return o;
    }

    /// <summary>Dumps the effective CLI values into an AppConfig for persistence.</summary>
    public void ApplyTo(AppConfig config, ISet<string> effectiveEnabled)
    {
        config.LastRootPath = RootPath;
        config.MinSizeText = MinSizeText;
        config.MinFileAgeDays = MinFileAgeDays;
        config.ScanLargeFiles = ScanLargeFiles;
        config.DetectGitStatus = DetectGit;
        config.UseRecycleBin = !PermanentDelete;
        config.EnabledFolderNames = effectiveEnabled.ToList();
        config.ExcludedFolderNames = ExcludedFolderNames.ToList();
        foreach (var c in CustomPatterns)
            if (!config.CustomPatterns.Any(p => p.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase)))
                config.CustomPatterns.Add(c);
    }

    private static IEnumerable<string> SplitList(string value)
        => value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static FolderPattern ParseCustom(string spec)
    {
        // Format: name|Category|Description  (category and description optional)
        var parts = spec.Split('|');
        var name = parts[0].Trim();
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException($"--custom requires a name: '{spec}'");
        var category = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : "Custom";
        var desc = parts.Length > 2 && parts[2].Trim().Length > 0 ? parts[2].Trim() : "Custom pattern";
        return new FolderPattern(name, category, desc, EnabledByDefault: true);
    }

    private static long SafeParseSize(string text, long fallback)
    {
        try { return SizeFormatter.ParseSize(text); }
        catch { return fallback; }
    }

    private static string RequireValue(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"The {option} option requires a value.");
        return args[++i];
    }

    /// <summary>Product name and version read from the assembly.</summary>
    public static string VersionInfo()
    {
        var asm = Assembly.GetExecutingAssembly();
        var product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                      ?? "Sweeft";
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "1.0.0";
        // Trim the build metadata suffix (e.g. "1.0.0+abc123").
        var plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        return $"{product} v{version}";
    }

    public static void PrintVersion() => System.Console.WriteLine(VersionInfo());

    public static void PrintUsage()
    {
        System.Console.WriteLine(VersionInfo() + "  —  a Jeffersoft tool");
        System.Console.WriteLine(
"""
Detects build/dependency folders and old, heavy files to free up disk space.
Shares its configuration with the graphical interface (GUI).

USAGE:
  sweeft <path> [options]

SCAN:
  -p, --path <path>     Root folder to analyze (or the first positional argument).
  -s, --min-size <val>  Minimum file size. E.g. 100MB, 1.5GB. (default: config)
  -a, --min-age <days>  Minimum file age in days. (default: config)
      --only-folders    Analyze folders only; skip files.
      --with-files      Force file analysis (opposite of --only-folders).
  -t, --types <list>    Detect ONLY these types. E.g. node_modules,bin,obj
  -x, --exclude <list>  Folders to skip entirely during traversal.
      --custom <spec>   Add a custom type. Format: name|Category|Description
                        (repeatable). E.g. --custom "logs|Other|Old logs"
      --git/--no-git    Enable/disable Git repository state detection.

OUTPUT:
      --list-types      List the catalog of detectable types and exit.
      --json            Print the scan result as JSON (does not delete).
      --report-only     Only show the report; never delete.

DELETION:
  -y, --yes             Do not ask; select EVERYTHING for deletion.
      --recycle         Send to the Recycle Bin (recoverable).
      --permanent       Permanent, irreversible deletion.
      --force           Required to combine --yes with --permanent (safety guard).

CONFIGURATION (shared with the GUI):
      --save-config     Save the parameters used as the new defaults.
      --config <path>   Use a specific configuration file.
      --no-config       Ignore the saved configuration (use base values).

  -h, --help            Show this help.
  -v, --version         Show the version and exit.

EXAMPLES:
  sweeft C:\Projects --list-types
  sweeft C:\Projects --types node_modules,bin,obj --no-git
  sweeft C:\Projects --min-size 500MB --min-age 365 --json
  sweeft C:\Projects --exclude .vs --save-config
  sweeft C:\Projects -y --recycle
""");
    }
}
