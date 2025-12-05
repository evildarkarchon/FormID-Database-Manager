using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using FormID_Database_Manager.ViewModels;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Handles various window-based operations such as file and folder selection
///     for the application using the provided IStorageProvider.
/// </summary>
public class WindowManager
{
    private readonly IStorageProvider _storageProvider;
    private readonly MainWindowViewModel _viewModel;

    /// <summary>
    ///     Manages window-related operations for the application, including file and directory selection.
    ///     Acts as a bridge between the UI and the backend services, utilizing Avalonia's IStorageProvider.
    /// </summary>
    public WindowManager(IStorageProvider storageProvider, MainWindowViewModel viewModel)
    {
        _storageProvider = storageProvider;
        _viewModel = viewModel;
    }

    /// <summary>
    ///     Allows the user to select a game directory through a folder picker dialog.
    ///     Returns the selected directory path or null if no selection is made or an error occurs.
    /// </summary>
    /// <returns>
    ///     A string representing the selected game's directory path or null if no folder is selected or an error occurs.
    /// </returns>
    public async Task<string?> SelectGameDirectory()
    {
        try
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Game Directory",
                AllowMultiple = false
            }).ConfigureAwait(false);

            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error selecting game directory: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Opens a file save dialog for the user to select or create a database file.
    ///     Allows saving with a default name, extension, and file type filtering for database files.
    /// </summary>
    /// <returns>
    ///     The local file system path of the selected database file, or null if the action is canceled or an error occurs.
    /// </returns>
    public async Task<string?> SelectDatabaseFile()
    {
        try
        {
            var fileTypeChoices = new List<FilePickerFileType>
            {
                new("Database Files") { Patterns = new[] { "*.db" } }
            };

            var options = new FilePickerSaveOptions
            {
                Title = "Select Database Location",
                DefaultExtension = "db",
                SuggestedFileName = "FormIDs.db",
                FileTypeChoices = fileTypeChoices
            };

            var file = await _storageProvider.SaveFilePickerAsync(options).ConfigureAwait(false);
            return file?.Path.LocalPath;
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error selecting database: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Opens a file picker dialog to allow the user to select a FormID list file.
    ///     Filters the files to display only text files with a .txt extension.
    ///     Returns the local path of the selected file or null if no file was selected.
    /// </summary>
    /// <returns>
    ///     A string containing the local path of the selected FormID list file, or null if no file was selected.
    /// </returns>
    public async Task<string?> SelectFormIdListFile()
    {
        try
        {
            var fileTypeChoices = new List<FilePickerFileType> { new("Text Files") { Patterns = new[] { "*.txt" } } };

            var options = new FilePickerOpenOptions
            {
                Title = "Select FormID List File",
                FileTypeFilter = fileTypeChoices,
                AllowMultiple = false
            };

            var files = await _storageProvider.OpenFilePickerAsync(options).ConfigureAwait(false);
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error selecting FormID list file: {ex.Message}");
            return null;
        }
    }
}
