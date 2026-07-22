using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Microsoft.UI.Windowing;
using Microsoft.Windows.Storage.Pickers;

namespace FormID_Database_Manager.WinUI.Services;

/// <summary>
/// Provides WinUI file and folder selection through Windows App SDK path-returning pickers.
/// </summary>
public sealed class WinUiFileDialogService : IFileDialogService
{
    private readonly AppWindow _appWindow;

    /// <summary>
    /// Creates a picker service that parents dialogs to the supplied WinUI <see cref="AppWindow.Id"/>.
    /// </summary>
    /// <param name="appWindow">The WinUI window whose ID is used as the picker owner.</param>
    public WinUiFileDialogService(AppWindow appWindow)
    {
        _appWindow = appWindow ?? throw new ArgumentNullException(nameof(appWindow));
    }

    /// <summary>
    /// Shows a WinUI folder picker for selecting a game installation or data directory.
    /// </summary>
    /// <returns>The picker outcome, distinguishing selection, cancellation, and platform failure.</returns>
    public async Task<FileDialogResult> SelectGameDirectory()
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
            return string.IsNullOrWhiteSpace(result?.Path)
                ? FileDialogResult.Cancelled()
                : FileDialogResult.Success(result.Path);
        }
        catch (Exception ex)
        {
            return FileDialogResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Shows a WinUI save picker configured for SQLite database output.
    /// </summary>
    /// <returns>The picker outcome, distinguishing selection, cancellation, and platform failure.</returns>
    public async Task<FileDialogResult> SelectDatabaseFile()
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
            return string.IsNullOrWhiteSpace(result?.Path)
                ? FileDialogResult.Cancelled()
                : FileDialogResult.Success(result.Path);
        }
        catch (Exception ex)
        {
            return FileDialogResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Shows a WinUI open picker configured for optional pipe-delimited FormID text files.
    /// </summary>
    /// <returns>The picker outcome, distinguishing selection, cancellation, and platform failure.</returns>
    public async Task<FileDialogResult> SelectFormIdListFile()
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
            return string.IsNullOrWhiteSpace(result?.Path)
                ? FileDialogResult.Cancelled()
                : FileDialogResult.Success(result.Path);
        }
        catch (Exception ex)
        {
            return FileDialogResult.Failure(ex.Message);
        }
    }
}
