using System.Text.Json;
using DepuradorCarpetas.Core;

namespace DepuradorCarpetas.ConsoleApp;

internal static class Program
{
    private static int Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;

        // No arguments: show help so the user can discover the options.
        if (args.Length == 0)
        {
            CliOptions.PrintUsage();
            return 0;
        }

        // 1) Load configuration (unless --no-config), locating its path first.
        var (peekPath, peekNoConfig) = CliOptions.PeekConfig(args);
        AppConfig config = peekNoConfig ? new AppConfig() : ConfigStore.Load(peekPath);

        // 2) Parse flags on top of the config.
        CliOptions cli;
        try
        {
            cli = CliOptions.Parse(args, config);
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            CliOptions.PrintUsage();
            return 2;
        }

        if (cli.ShowVersion)
        {
            CliOptions.PrintVersion();
            return 0;
        }

        if (cli.ShowHelp)
        {
            CliOptions.PrintUsage();
            return 0;
        }

        // 3) Build the effective catalog (built-in + custom from config and CLI).
        var catalog = new List<FolderPattern>(config.AllPatterns());
        foreach (var c in cli.CustomPatterns)
            if (!catalog.Any(p => p.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase)))
                catalog.Add(c);

        if (cli.ListTypes)
        {
            PrintTypes(catalog, config.ResolveEnabled());
            return 0;
        }

        if (!Directory.Exists(cli.RootPath))
        {
            WriteError($"The folder does not exist: {cli.RootPath}");
            return 2;
        }

        // 4) Resolve the enabled types.
        ISet<string> enabled;
        if (cli.ExplicitTypes is not null)
        {
            enabled = cli.ExplicitTypes;
        }
        else
        {
            enabled = config.ResolveEnabled();
            foreach (var c in cli.CustomPatterns) enabled.Add(c.Name); // CLI-added ones are enabled
        }

        var scanOptions = new ScanOptions
        {
            RootPath = cli.RootPath,
            MinLargeFileBytes = cli.MinLargeFileBytes,
            MinFileAgeDays = cli.MinFileAgeDays,
            ScanLargeFiles = cli.ScanLargeFiles,
            FolderPatterns = catalog,
            EnabledFolderNames = enabled,
            DetectGitStatus = cli.DetectGit,
            ExcludedFolderNames = cli.ExcludedFolderNames,
        };

        if (!cli.JsonOutput)
            PrintHeader(cli, scanOptions);

        var scanner = new FolderScanner(scanOptions);
        ScanResult result;
        try
        {
            IProgress<string>? progress = cli.JsonOutput ? null : MakeProgress();
            result = scanner.Scan(progress);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine();
            WriteError($"Error during the scan: {ex.Message}");
            return 1;
        }

        if (!cli.JsonOutput)
            ClearProgressLine();

        // 5) Persist configuration if requested.
        if (cli.SaveConfig && !cli.NoConfig)
        {
            cli.ApplyTo(config, enabled);
            try
            {
                ConfigStore.Save(config, cli.ConfigPath);
                if (!cli.JsonOutput)
                    System.Console.WriteLine($"Configuration saved to: {cli.ConfigPath ?? ConfigStore.DefaultPath}");
            }
            catch (Exception ex)
            {
                WriteError($"Could not save the configuration: {ex.Message}");
            }
        }

        // 6) Output.
        if (cli.JsonOutput)
        {
            PrintJson(result);
            return 0;
        }

        PrintReport(result);

        if (result.Findings.Count == 0)
        {
            System.Console.WriteLine("No candidate items were found. Nothing to do.");
            return 0;
        }

        if (cli.ReportOnly)
        {
            System.Console.WriteLine("Report-only mode: nothing will be deleted.");
            return 0;
        }

        return RunDeletionFlow(result, cli);
    }

    private static int RunDeletionFlow(ScanResult result, CliOptions cli)
    {
        var mode = cli.PermanentDelete ? DeleteMode.Permanent : DeleteMode.RecycleBin;

        // Safety guard: refuse unattended permanent deletion unless explicitly forced.
        if (cli.AssumeYes && cli.PermanentDelete && !cli.Force)
        {
            WriteError("Refusing to permanently delete without confirmation. " +
                       "Add --force to combine --yes with --permanent, or use --recycle.");
            return 2;
        }

        var modeLabel = mode == DeleteMode.Permanent
            ? "PERMANENT DELETION (irreversible)"
            : "send to the Recycle Bin (recoverable)";

        System.Console.WriteLine();
        System.Console.WriteLine($"Action: {modeLabel}.");

        List<Finding> toDelete;
        if (cli.AssumeYes)
        {
            toDelete = result.Findings.ToList();
        }
        else
        {
            toDelete = InteractiveSelection(result);
            if (toDelete.Count == 0)
            {
                System.Console.WriteLine("Nothing selected. Operation cancelled.");
                return 0;
            }

            long selectedBytes = toDelete.Sum(f => f.SizeBytes);
            int dirty = toDelete.Count(f => f.RepoStatus == GitRepoStatus.Dirty);
            if (dirty > 0)
                WriteColored(ConsoleColor.Yellow,
                    $"⚠ {dirty} item(s) are in repositories with uncommitted changes.");

            System.Console.WriteLine();
            System.Console.Write($"{toDelete.Count} item(s) will be deleted " +
                                 $"({SizeFormatter.Humanize(selectedBytes)}) via {modeLabel}. Continue? [y/N]: ");
            var answer = System.Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer is not ("y" or "yes"))
            {
                System.Console.WriteLine("Operation cancelled.");
                return 0;
            }
        }

        var cleaner = new Cleaner();
        long freed = 0;
        int ok = 0, failed = 0, done = 0;
        int total = toDelete.Count;

        cleaner.DeleteMany(toDelete, mode, outcome =>
        {
            done++;
            var prefix = $"[{done}/{total}]".PadRight(10);
            if (outcome.Success)
            {
                ok++;
                freed += outcome.FreedBytes;
                WriteColored(ConsoleColor.Green, $"  {prefix} [OK]   {outcome.Path}");
            }
            else
            {
                failed++;
                WriteColored(ConsoleColor.Red, $"  {prefix} [FAIL] {outcome.Path} -> {outcome.Error}");
            }
        });

        System.Console.WriteLine();
        System.Console.WriteLine($"Done. {ok} deleted, {failed} failed. " +
                                 $"Space freed: {SizeFormatter.Humanize(freed)}.");
        return failed == 0 ? 0 : 1;
    }

    private static List<Finding> InteractiveSelection(ScanResult result)
    {
        System.Console.WriteLine();
        System.Console.WriteLine("What do you want to delete?");
        System.Console.WriteLine("  [1] Build/dependency/cache folders only");
        System.Console.WriteLine("  [2] Old, large files only");
        System.Console.WriteLine("  [3] Both (everything listed)");
        System.Console.WriteLine("  [0] Cancel");
        System.Console.Write("Option: ");
        var choice = System.Console.ReadLine()?.Trim();

        return choice switch
        {
            "1" => result.JunkFolders.ToList(),
            "2" => result.LargeOldFiles.ToList(),
            "3" => result.Findings.ToList(),
            _ => new List<Finding>(),
        };
    }

    // ---------- Output ----------

    private static void PrintTypes(IReadOnlyList<FolderPattern> catalog, ISet<string> enabled)
    {
        System.Console.WriteLine("Detectable folder types (✓ = enabled by default per your configuration):");
        System.Console.WriteLine();
        foreach (var group in catalog.GroupBy(p => p.Category))
        {
            WriteColored(ConsoleColor.Cyan, $"── {group.Key} ──");
            foreach (var p in group)
            {
                var mark = enabled.Contains(p.Name) ? "✓" : " ";
                System.Console.WriteLine($"  [{mark}] {p.Name,-18} {p.Description}");
            }
            System.Console.WriteLine();
        }
    }

    private static void PrintJson(ScanResult result)
    {
        var payload = new
        {
            totalReclaimableBytes = result.TotalReclaimableBytes,
            totalReclaimableHuman = SizeFormatter.Humanize(result.TotalReclaimableBytes),
            count = result.Findings.Count,
            warnings = result.Warnings,
            findings = result.Findings.Select(f => new
            {
                kind = f.Kind.ToString(),
                path = f.Path,
                sizeBytes = f.SizeBytes,
                sizeHuman = f.HumanSize,
                ageDays = f.AgeDays,
                lastModifiedUtc = f.LastModifiedUtc,
                reason = f.Reason,
                repoRoot = f.RepoRoot,
                repoStatus = f.RepoStatus.ToString(),
            }),
        };
        System.Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    private static void PrintHeader(CliOptions cli, ScanOptions opts)
    {
        System.Console.WriteLine("=== Sweeft ===");
        System.Console.WriteLine($"Root      : {cli.RootPath}");
        System.Console.WriteLine($"Files     : {(opts.ScanLargeFiles ? $"> {SizeFormatter.Humanize(opts.MinLargeFileBytes)} and > {opts.MinFileAgeDays} days" : "skipped")}");
        System.Console.WriteLine($"Git       : {(opts.DetectGitStatus ? "detection on" : "off")}");
        if (opts.ExcludedFolderNames.Count > 0)
            System.Console.WriteLine($"Excluded  : {string.Join(", ", opts.ExcludedFolderNames)}");
        System.Console.WriteLine();
    }

    private static void PrintReport(ScanResult result)
    {
        void Section(string title, IEnumerable<Finding> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            System.Console.WriteLine();
            WriteColored(ConsoleColor.Cyan, $"── {title} ({list.Count}) ──");
            foreach (var f in list)
            {
                var size = f.HumanSize.PadLeft(10);
                var age = $"{f.AgeDays,5} d";
                System.Console.WriteLine($"  {size}  {age}  {f.Path}");
                var repo = f.RepoStatus switch
                {
                    GitRepoStatus.Dirty => "  [repo: UNCOMMITTED CHANGES]",
                    GitRepoStatus.Clean => "  [repo: clean]",
                    GitRepoStatus.Unknown => "  [repo: unchecked]",
                    _ => "",
                };
                System.Console.WriteLine($"              {f.Reason}{repo}");
            }
        }

        Section("Build / dependency / cache folders", result.JunkFolders);
        Section("Old, large files", result.LargeOldFiles);

        System.Console.WriteLine();
        WriteColored(ConsoleColor.Yellow,
            $"Total reclaimable: {SizeFormatter.Humanize(result.TotalReclaimableBytes)} " +
            $"across {result.Findings.Count} item(s).");

        if (result.Warnings.Count > 0)
        {
            System.Console.WriteLine();
            WriteColored(ConsoleColor.DarkYellow, $"Warnings ({result.Warnings.Count}):");
            foreach (var w in result.Warnings.Take(15))
                System.Console.WriteLine($"  - {w}");
            if (result.Warnings.Count > 15)
                System.Console.WriteLine($"  ... and {result.Warnings.Count - 15} more.");
        }
    }

    // ---------- Console utilities ----------

    private static IProgress<string> MakeProgress()
    {
        var last = DateTime.MinValue;
        return new Progress<string>(path =>
        {
            if ((DateTime.UtcNow - last).TotalMilliseconds < 120) return;
            last = DateTime.UtcNow;
            int width = SafeWindowWidth();
            var line = Truncate($"  Scanning: {path}", width - 1);
            System.Console.Write("\r" + line.PadRight(width - 1));
        });
    }

    private static void ClearProgressLine()
    {
        int width = SafeWindowWidth();
        System.Console.Write("\r" + new string(' ', Math.Max(1, width - 1)) + "\r");
    }

    private static int SafeWindowWidth()
    {
        try { return Math.Max(20, System.Console.WindowWidth); }
        catch { return 80; }
    }

    private static string Truncate(string text, int max)
        => max <= 1 || text.Length <= max ? text : text[..(max - 1)] + "…";

    private static void WriteColored(ConsoleColor color, string text)
    {
        var prev = System.Console.ForegroundColor;
        System.Console.ForegroundColor = color;
        System.Console.WriteLine(text);
        System.Console.ForegroundColor = prev;
    }

    private static void WriteError(string message) => WriteColored(ConsoleColor.Red, "Error: " + message);
}
