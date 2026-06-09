using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.Windows.Storage.Pickers;

namespace FormID_Database_Manager.WinUI.Services;

/// <summary>
/// Provides WinUI file and folder selection through Windows App SDK path-returning pickers.
/// </summary>
public sealed class WinUiFileDialogService : IFileDialogService
{
    private readonly AppWindow _appWindow;
    private readonly MainWindowViewModel _viewModel;

    /// <summary>
    /// Creates a picker service that parents dialogs to the supplied WinUI <see cref="AppWindow.Id"/>.
    /// </summary>
    /// <param name="appWindow">The WinUI window whose ID is used as the picker owner.</param>
    /// <param name="viewModel">The ViewModel that receives picker error messages.</param>
    public WinUiFileDialogService(AppWindow appWindow, MainWindowViewModel viewModel)
    {
        _appWindow = appWindow ?? throw new ArgumentNullException(nameof(appWindow));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    /// <summary>
    /// Shows a WinUI folder picker for selecting a game installation or data directory.
    /// </summary>
    /// <returns>The selected folder path, or <see langword="null"/> when canceled or unavailable.</returns>
    public async Task<string?> SelectGameDirectory()
    {
        try
        {
            var picker = new FolderPicker(_appWindow.Id)
            {
                CommitButtonText = "Select Folder",
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                ViewMode = PickerViewMode.List
            };

            var result = await picker.PickSingleFolderAsync();
            return string.IsNullOrWhiteSpace(result?.Path) ? null : result.Path;
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error selecting game directory: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Shows a WinUI save picker configured for SQLite database output.
    /// </summary>
    /// <returns>The selected database path, or <see langword="null"/> when canceled or unavailable.</returns>
    public async Task<string?> SelectDatabaseFile()
    {
        try
        {
            var picker = new FileSavePicker(_appWindow.Id)
            {
                DefaultFileExtension = ".db",
                SuggestedFileName = "FormIDs.db",
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                FileTypeChoices =
                {
                    { "Database Files", new List<string> { ".db" } }
                }
            };

            var result = await picker.PickSaveFileAsync();
            return string.IsNullOrWhiteSpace(result?.Path) ? null : result.Path;
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error selecting database: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Shows a WinUI open picker configured for optional pipe-delimited FormID text files.
    /// </summary>
    /// <returns>The selected text file path, or <see langword="null"/> when canceled or unavailable.</returns>
    public async Task<string?> SelectFormIdListFile()
    {
        try
        {
            var picker = new FileOpenPicker(_appWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List,
                FileTypeFilter = { ".txt" }
            };

            var result = await picker.PickSingleFileAsync();
            return string.IsNullOrWhiteSpace(result?.Path) ? null : result.Path;
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error selecting FormID list file: {ex.Message}");
            return null;
        }
    }
}
