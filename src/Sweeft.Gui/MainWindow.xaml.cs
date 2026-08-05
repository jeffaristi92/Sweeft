using System.IO;
using System.Windows;
using Sweeft.Gui.ViewModels;
using Microsoft.Win32;

namespace Sweeft.Gui;

/// <summary>Main window of Sweeft.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closing += (_, _) => _viewModel.SaveConfigSilently();

        // Bridge the custom-drawn treemap to the view model.
        _viewModel.DiskUsageUpdated += () =>
            Treemap.SetItems(_viewModel.DiskUsageItems.ToList(), _viewModel.DiskUsageTotal);
        Treemap.ItemActivated += async item => await _viewModel.DrillIntoAsync(item);
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the folder to analyze",
        };
        if (Directory.Exists(_viewModel.RootPath))
            dialog.InitialDirectory = _viewModel.RootPath;

        if (dialog.ShowDialog(this) == true)
            _viewModel.RootPath = dialog.FolderName;
    }
}
