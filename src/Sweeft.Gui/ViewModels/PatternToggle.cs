using Sweeft.Core;
using Sweeft.Gui.Mvvm;

namespace Sweeft.Gui.ViewModels;

/// <summary>Represents a detectable folder type that the user can enable/disable.</summary>
public sealed class PatternToggle : ObservableObject
{
    public PatternToggle(FolderPattern pattern, bool isEnabled, bool isCustom)
    {
        Name = pattern.Name;
        Category = pattern.Category;
        Description = pattern.Description;
        IsCustom = isCustom;
        _isEnabled = isEnabled;
    }

    public string Name { get; }
    public string Category { get; }
    public string Description { get; }

    /// <summary>True if the user defined it (persisted as a custom pattern).</summary>
    public bool IsCustom { get; }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>Text shown next to the checkbox.</summary>
    public string Display => IsCustom
        ? $"{Name}  —  {Description}  (custom)"
        : $"{Name}  —  {Description}";

    public FolderPattern ToPattern() => new(Name, Category, Description, EnabledByDefault: true);
}
