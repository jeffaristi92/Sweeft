using System.Windows.Media;
using Sweeft.Core;
using Sweeft.Gui.Mvvm;

namespace Sweeft.Gui.ViewModels;

/// <summary>Wrapper around a <see cref="Finding"/> with selection state for the grid.</summary>
public sealed class FindingViewModel : ObservableObject
{
    public FindingViewModel(Finding model)
    {
        Model = model;
        // Safe pre-selection: regenerable folders that are NOT in a repo with
        // uncommitted changes, and global caches (always regenerable). Large
        // files and anything in "dirty" repos stay unchecked so the user decides.
        _isSelected = model.Kind == FindingKind.GlobalCache
                      || (model.Kind == FindingKind.JunkFolder && model.RepoStatus != GitRepoStatus.Dirty);
    }

    public Finding Model { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string KindLabel => Model.Kind switch
    {
        FindingKind.JunkFolder => "Folder",
        FindingKind.GlobalCache => "Cache",
        _ => "File",
    };
    public string Description => Model.Reason;
    public string HumanSize => Model.HumanSize;
    public long SizeBytes => Model.SizeBytes;
    public int AgeDays => Model.AgeDays;
    public string Path => Model.Path;

    public bool IsRisky => Model.RepoStatus == GitRepoStatus.Dirty;

    public string RepoStatusText => Model.RepoStatus switch
    {
        GitRepoStatus.Dirty   => "⚠ Uncommitted changes",
        GitRepoStatus.Clean   => "✓ Clean",
        GitRepoStatus.Unknown => "Repo (unchecked)",
        _                     => "—",
    };

    public Brush RepoStatusBrush => Model.RepoStatus switch
    {
        GitRepoStatus.Dirty   => Brushes.OrangeRed,
        GitRepoStatus.Clean   => Brushes.SeaGreen,
        GitRepoStatus.Unknown => Brushes.Gray,
        _                     => Brushes.DarkGray,
    };
}
