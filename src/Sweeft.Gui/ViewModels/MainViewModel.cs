using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using Sweeft.Core;
using Sweeft.Gui;
using Sweeft.Gui.Mvvm;

namespace Sweeft.Gui.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly GitService _git = new();
    private readonly AppConfig _config;
    private CancellationTokenSource? _cts;

    public MainViewModel()
    {
        _config = ConfigStore.Load();
        LoadFromConfig(_config);

        // Grouped view by category to present the types in an ordered way.
        PatternsView = CollectionViewSource.GetDefaultView(Patterns);
        PatternsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PatternToggle.Category)));

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && (ScanGlobalCaches || Directory.Exists(RootPath)));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !IsBusy && SelectedCount > 0);
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true), () => Findings.Count > 0);
        SelectNoneCommand = new RelayCommand(() => SetAllSelected(false), () => Findings.Count > 0);
        SelectFoldersCommand = new RelayCommand(SelectOnlyFolders, () => Findings.Count > 0);
        AddCustomTypeCommand = new RelayCommand(AddCustomType, () => !string.IsNullOrWhiteSpace(NewTypeName));
        SaveConfigCommand = new RelayCommand(SaveConfigManually, () => !IsBusy);
        DiskUsageUpCommand = new AsyncRelayCommand(DiskUsageUpAsync, () => !IsBusy && CanGoUp);
        StopCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);

        GitAvailableText = _git.IsGitAvailable
            ? "Git detected: repository state will be shown."
            : "Git is not installed: repositories will be marked as 'unchecked'.";
    }

    // ---- Scan configuration ----
    private string _rootPath = "";
    public string RootPath
    {
        get => _rootPath;
        set { if (SetProperty(ref _rootPath, value)) OnPropertyChanged(nameof(CanScanHint)); }
    }

    public ObservableCollection<PatternToggle> Patterns { get; } = new();
    public ICollectionView PatternsView { get; private set; } = null!;

    private bool _scanLargeFiles = true;
    public bool ScanLargeFiles { get => _scanLargeFiles; set => SetProperty(ref _scanLargeFiles, value); }

    private string _minSizeText = "100MB";
    public string MinSizeText { get => _minSizeText; set => SetProperty(ref _minSizeText, value); }

    private int _minAgeDays = 180;
    public int MinAgeDays { get => _minAgeDays; set => SetProperty(ref _minAgeDays, value); }

    private bool _detectGit = true;
    public bool DetectGit { get => _detectGit; set => SetProperty(ref _detectGit, value); }

    private bool _scanGlobalCaches;
    public bool ScanGlobalCaches { get => _scanGlobalCaches; set => SetProperty(ref _scanGlobalCaches, value); }

    // ---- Disk-usage (treemap) mode ----
    private bool _diskUsageMode;
    public bool DiskUsageMode
    {
        get => _diskUsageMode;
        set { if (SetProperty(ref _diskUsageMode, value)) OnPropertyChanged(nameof(CleanupMode)); }
    }
    /// <summary>Inverse of <see cref="DiskUsageMode"/> (for showing the cleanup UI).</summary>
    public bool CleanupMode => !DiskUsageMode;

    public ObservableCollection<TreemapItem> DiskUsageItems { get; } = new();
    /// <summary>Raised after the treemap items change, so the view can re-render.</summary>
    public event Action? DiskUsageUpdated;

    private readonly Stack<string> _duStack = new();

    private long _diskUsageTotal;
    public long DiskUsageTotal { get => _diskUsageTotal; private set => SetProperty(ref _diskUsageTotal, value); }

    private string _diskUsagePathText = "";
    public string DiskUsagePathText { get => _diskUsagePathText; private set => SetProperty(ref _diskUsagePathText, value); }

    public bool CanGoUp => _duStack.Count > 1;

    private bool _onlyStaleProjects;
    public bool OnlyStaleProjects { get => _onlyStaleProjects; set => SetProperty(ref _onlyStaleProjects, value); }

    private string _staleText = "90d";
    public string StaleText { get => _staleText; set => SetProperty(ref _staleText, value); }

    private string _excludedNamesText = "";
    public string ExcludedNamesText { get => _excludedNamesText; set => SetProperty(ref _excludedNamesText, value); }

    public string GitAvailableText { get; }

    // ---- Custom type input ----
    private string _newTypeName = "";
    public string NewTypeName { get => _newTypeName; set => SetProperty(ref _newTypeName, value); }
    private string _newTypeDescription = "";
    public string NewTypeDescription { get => _newTypeDescription; set => SetProperty(ref _newTypeDescription, value); }

    // ---- Results ----
    public ObservableCollection<FindingViewModel> Findings { get; } = new();

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(IsIdle)); }
    }
    public bool IsIdle => !IsBusy;

    private string _statusText = "Select a folder and click «Scan».";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    // Progress: indeterminate during the scan, determinate during deletion.
    private bool _isIndeterminate = true;
    public bool IsIndeterminate { get => _isIndeterminate; set => SetProperty(ref _isIndeterminate, value); }
    private double _progressValue;
    public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }
    private double _progressMax = 1;
    public double ProgressMax { get => _progressMax; set => SetProperty(ref _progressMax, value); }

    private int _selectedCount;
    public int SelectedCount { get => _selectedCount; private set => SetProperty(ref _selectedCount, value); }

    private string _selectedSizeText = "0 B";
    public string SelectedSizeText { get => _selectedSizeText; private set => SetProperty(ref _selectedSizeText, value); }

    private string _totalText = "";
    public string TotalText { get => _totalText; private set => SetProperty(ref _totalText, value); }

    private bool _useRecycleBin = true;
    public bool UseRecycleBin { get => _useRecycleBin; set => SetProperty(ref _useRecycleBin, value); }

    public string CanScanHint => string.IsNullOrWhiteSpace(RootPath) || Directory.Exists(RootPath)
        ? "" : "The specified folder does not exist.";

    // ---- Commands ----
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand SelectFoldersCommand { get; }
    public RelayCommand AddCustomTypeCommand { get; }
    public RelayCommand SaveConfigCommand { get; }
    public AsyncRelayCommand DiskUsageUpCommand { get; }
    public RelayCommand StopCommand { get; }

    // ---- Config <-> VM ----
    private void LoadFromConfig(AppConfig config)
    {
        RootPath = config.LastRootPath ?? "";
        MinSizeText = config.MinSizeText;
        MinAgeDays = config.MinFileAgeDays;
        ScanLargeFiles = config.ScanLargeFiles;
        DetectGit = config.DetectGitStatus;
        UseRecycleBin = config.UseRecycleBin;
        OnlyStaleProjects = !string.IsNullOrWhiteSpace(config.StaleText);
        StaleText = string.IsNullOrWhiteSpace(config.StaleText) ? "90d" : config.StaleText;
        ExcludedNamesText = string.Join(", ", config.ExcludedFolderNames);

        var enabled = config.ResolveEnabled();
        var customNames = new HashSet<string>(
            config.CustomPatterns.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        Patterns.Clear();
        foreach (var pattern in config.AllPatterns())
            Patterns.Add(new PatternToggle(pattern, enabled.Contains(pattern.Name), customNames.Contains(pattern.Name)));
    }

    private void ApplyToConfig()
    {
        _config.LastRootPath = RootPath;
        _config.MinSizeText = MinSizeText;
        _config.MinFileAgeDays = MinAgeDays;
        _config.ScanLargeFiles = ScanLargeFiles;
        _config.DetectGitStatus = DetectGit;
        _config.UseRecycleBin = UseRecycleBin;
        _config.StaleText = OnlyStaleProjects ? StaleText.Trim() : "";
        _config.EnabledFolderNames = Patterns.Where(p => p.IsEnabled).Select(p => p.Name).ToList();
        _config.CustomPatterns = Patterns.Where(p => p.IsCustom).Select(p => p.ToPattern()).ToList();
        _config.ExcludedFolderNames = ParseExcluded().ToList();
    }

    /// <summary>Saves the configuration; swallows errors so the app is not interrupted.</summary>
    public void SaveConfigSilently()
    {
        try
        {
            ApplyToConfig();
            ConfigStore.Save(_config);
        }
        catch { /* non-critical */ }
    }

    private void SaveConfigManually()
    {
        try
        {
            ApplyToConfig();
            ConfigStore.Save(_config);
            StatusText = $"Configuration saved to {ConfigStore.DefaultPath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the configuration:\n{ex.Message}",
                "Sweeft", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private IEnumerable<string> ParseExcluded()
        => ExcludedNamesText
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void AddCustomType()
    {
        var name = NewTypeName.Trim();
        if (name.Length == 0) return;

        if (Patterns.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"A type named '{name}' already exists.", "Sweeft",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var desc = string.IsNullOrWhiteSpace(NewTypeDescription) ? "Custom pattern" : NewTypeDescription.Trim();
        var pattern = new FolderPattern(name, "Custom", desc, EnabledByDefault: true);
        Patterns.Add(new PatternToggle(pattern, isEnabled: true, isCustom: true));
        PatternsView.Refresh();

        NewTypeName = "";
        NewTypeDescription = "";
        StatusText = $"Custom type '{name}' added. Remember to scan.";
    }

    // ---- Logic ----
    private async Task ScanAsync()
    {
        if (DiskUsageMode)
        {
            if (!Directory.Exists(RootPath))
            {
                MessageBox.Show("The specified folder does not exist.", "Sweeft",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _duStack.Clear();
            _duStack.Push(RootPath);
            await LoadDiskUsageAsync(RootPath);
            return;
        }

        if (ScanGlobalCaches)
        {
            await ScanGlobalAsync();
            return;
        }

        if (!Directory.Exists(RootPath))
        {
            MessageBox.Show("The specified folder does not exist.", "Sweeft",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        long minSize;
        try { minSize = SizeFormatter.ParseSize(MinSizeText); }
        catch
        {
            MessageBox.Show($"Invalid minimum size: '{MinSizeText}'. Use formats like 100MB or 1.5GB.",
                "Sweeft", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var catalog = Patterns.Select(p => p.ToPattern()).ToList();
        var enabled = new HashSet<string>(
            Patterns.Where(p => p.IsEnabled).Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        if (enabled.Count == 0 && !ScanLargeFiles)
        {
            MessageBox.Show("Nothing is selected to detect (neither folder types nor files).",
                "Sweeft", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TimeSpan? staleThreshold = null;
        if (OnlyStaleProjects)
        {
            if (!DurationParser.TryParse(StaleText, out var ts))
            {
                MessageBox.Show($"Invalid stale window: '{StaleText}'. Use formats like 90d, 2w, 6mo, 1y.",
                    "Sweeft", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            staleThreshold = ts;
        }

        var options = new ScanOptions
        {
            RootPath = RootPath,
            MinLargeFileBytes = minSize,
            MinFileAgeDays = MinAgeDays,
            ScanLargeFiles = ScanLargeFiles,
            FolderPatterns = catalog,
            EnabledFolderNames = enabled,
            DetectGitStatus = DetectGit,
            ExcludedFolderNames = new HashSet<string>(ParseExcluded(), StringComparer.OrdinalIgnoreCase),
            StaleProjectThreshold = staleThreshold,
        };

        IsBusy = true;
        IsIndeterminate = true;
        Findings.Clear();
        RecomputeSelection();
        TotalText = "";
        StatusText = "Scanning…";

        var progress = new Progress<string>(msg => StatusText = Truncate(msg, 90));

        try
        {
            var token = BeginScan();
            var scanner = new FolderScanner(options, _git);
            ScanResult result = await Task.Run(() => scanner.Scan(progress, token), token);

            foreach (var f in result.Findings)
            {
                var vm = new FindingViewModel(f);
                vm.PropertyChanged += OnFindingPropertyChanged;
                Findings.Add(vm);
            }

            RecomputeSelection();

            var dirty = result.Findings.Count(f => f.RepoStatus == GitRepoStatus.Dirty);
            TotalText = $"{result.Findings.Count} item(s) · " +
                        $"{SizeFormatter.Humanize(result.TotalReclaimableBytes)} reclaimable" +
                        (dirty > 0 ? $" · {dirty} in repos with uncommitted changes" : "");

            StatusText = result.Findings.Count == 0
                ? "No candidate items were found."
                : $"Scan complete. {result.Warnings.Count} warning(s).";

            // Persist the configuration used (remember preferences across sessions).
            SaveConfigSilently();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during the scan:\n{ex.Message}", "Sweeft",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Scan interrupted by an error.";
        }
        finally
        {
            EndScan();
        }
    }

    /// <summary>Drill into a directory in the treemap (called from the view on double-click).</summary>
    public async Task DrillIntoAsync(TreemapItem item)
    {
        if (IsBusy || !item.IsDirectory) return;
        _duStack.Push(item.Path);
        await LoadDiskUsageAsync(item.Path);
    }

    private async Task DiskUsageUpAsync()
    {
        if (IsBusy || _duStack.Count <= 1) return;
        _duStack.Pop();
        await LoadDiskUsageAsync(_duStack.Peek());
    }

    private async Task LoadDiskUsageAsync(string path)
    {
        IsBusy = true;
        IsIndeterminate = true;
        StatusText = "Measuring disk usage…";
        var progress = new Progress<string>(msg => StatusText = Truncate(msg, 90));

        try
        {
            var token = BeginScan();
            var scanner = new DiskUsageScanner();
            DiskUsageResult result = await Task.Run(() => scanner.Scan(path, progress, token), token);

            DiskUsageItems.Clear();
            foreach (var e in result.Entries)
                DiskUsageItems.Add(new TreemapItem(e.Name, e.Path, e.SizeBytes, e.IsDirectory));

            DiskUsageTotal = result.TotalBytes;
            DiskUsagePathText = path;
            OnPropertyChanged(nameof(CanGoUp));
            StatusText = $"{result.Entries.Count} item(s) · {SizeFormatter.Humanize(result.TotalBytes)} · double-click a folder to drill in.";
            DiskUsageUpdated?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error measuring disk usage:\n{ex.Message}", "Sweeft",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Scan interrupted by an error.";
        }
        finally
        {
            EndScan();
        }
    }

    private async Task ScanGlobalAsync()
    {
        IsBusy = true;
        IsIndeterminate = true;
        Findings.Clear();
        RecomputeSelection();
        TotalText = "";
        StatusText = "Scanning global caches…";

        var progress = new Progress<string>(msg => StatusText = Truncate(msg, 90));

        try
        {
            var token = BeginScan();
            var scanner = new GlobalCacheScanner();
            ScanResult result = await Task.Run(() => scanner.Scan(progress, token), token);

            foreach (var f in result.Findings)
            {
                var vm = new FindingViewModel(f);
                vm.PropertyChanged += OnFindingPropertyChanged;
                Findings.Add(vm);
            }

            RecomputeSelection();
            TotalText = $"{result.Findings.Count} cache(s) · " +
                        $"{SizeFormatter.Humanize(result.TotalReclaimableBytes)} reclaimable";
            StatusText = result.Findings.Count == 0
                ? "No global caches found."
                : "Global cache scan complete. These are safe to delete — they rebuild on demand.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during the scan:\n{ex.Message}", "Sweeft",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Scan interrupted by an error.";
        }
        finally
        {
            EndScan();
        }
    }

    private async Task DeleteAsync()
    {
        var selected = Findings.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        var mode = UseRecycleBin ? DeleteMode.RecycleBin : DeleteMode.Permanent;
        var modeLabel = UseRecycleBin
            ? "will be sent to the Recycle Bin (recoverable)"
            : "will be PERMANENTLY DELETED (irreversible)";

        long bytes = selected.Sum(f => f.SizeBytes);
        int dirty = selected.Count(f => f.IsRisky);
        var warn = dirty > 0
            ? $"\n\n⚠ Warning: {dirty} of these items are in repositories with uncommitted changes."
            : "";

        var confirm = MessageBox.Show(
            $"{selected.Count} item(s) will be deleted ({SizeFormatter.Humanize(bytes)}).\n" +
            $"The items {modeLabel}.{warn}\n\nDo you want to continue?",
            "Confirm cleanup",
            MessageBoxButton.YesNo,
            dirty > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        IsIndeterminate = false;
        ProgressMax = selected.Count;
        ProgressValue = 0;
        StatusText = "Deleting…";

        var dispatcher = Application.Current.Dispatcher;

        try
        {
            var cleaner = new Cleaner();
            long freed = 0;
            int ok = 0, failed = 0, done = 0;
            var errors = new List<string>();

            await Task.Run(() =>
            {
                foreach (var vm in selected)
                {
                    var outcome = cleaner.Delete(vm.Model, mode);
                    done++;
                    int localDone = done;

                    if (outcome.Success)
                    {
                        ok++;
                        freed += outcome.FreedBytes;
                    }
                    else
                    {
                        failed++;
                        errors.Add($"{vm.Path}: {outcome.Error}");
                    }

                    // Report progress to the UI thread.
                    dispatcher.Invoke(() =>
                    {
                        ProgressValue = localDone;
                        StatusText = $"Deleting {localDone}/{selected.Count}: {Truncate(vm.Path, 70)}";
                        if (outcome.Success)
                        {
                            vm.PropertyChanged -= OnFindingPropertyChanged;
                            Findings.Remove(vm);
                        }
                    });
                }
            });

            RecomputeSelection();
            StatusText = $"Done. {ok} deleted, {failed} failed. " +
                         $"Space freed: {SizeFormatter.Humanize(freed)}.";

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    "Some items could not be deleted:\n\n" +
                    string.Join("\n", errors.Take(10)) +
                    (errors.Count > 10 ? $"\n… and {errors.Count - 10} more." : ""),
                    "Cleanup with errors", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = true;
        }
    }

    private void OnFindingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FindingViewModel.IsSelected))
            RecomputeSelection();
    }

    private void SetAllSelected(bool value)
    {
        foreach (var f in Findings) f.IsSelected = value;
        RecomputeSelection();
    }

    private void SelectOnlyFolders()
    {
        foreach (var f in Findings)
            f.IsSelected = f.Model.Kind == FindingKind.JunkFolder;
        RecomputeSelection();
    }

    private void RecomputeSelection()
    {
        var selected = Findings.Where(f => f.IsSelected).ToList();
        SelectedCount = selected.Count;
        SelectedSizeText = SizeFormatter.Humanize(selected.Sum(f => f.SizeBytes));
    }

    private CancellationToken BeginScan()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    private void EndScan()
    {
        IsBusy = false;
        _cts?.Dispose();
        _cts = null;
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : "…" + text[^(max - 1)..];
}
