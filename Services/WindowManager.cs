using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;

namespace FormID_Database_Manager.Services;

public class WindowManager
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IStorageProvider _storageProvider;

    public WindowManager(IStorageProvider storageProvider, MainWindowViewModel viewModel)
    {
        _storageProvider = storageProvider;
        _viewModel = viewModel;
    }

    public async Task<string?> SelectGameDirectory()
    {
        try
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Game Directory",
                AllowMultiple = false
            });

            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error selecting game directory: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> SelectDatabaseFile()
    {
        try
        {
            var fileTypeChoices = new List<FilePickerFileType>
            {
                new("Database Files")
                {
                    Patterns = new[] { "*.db" }
                }
            };

            var options = new FilePickerSaveOptions
            {
                Title = "Select Database Location",
                DefaultExtension = "db",
                SuggestedFileName = "FormIDs.db",
                FileTypeChoices = fileTypeChoices
            };

            var file = await _storageProvider.SaveFilePickerAsync(options);
            return file?.Path.LocalPath;
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error selecting database: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> SelectFormIdListFile()
    {
        try
        {
            var fileTypeChoices = new List<FilePickerFileType>
            {
                new("Text Files")
                {
                    Patterns = new[] { "*.txt" }
                }
            };

            var options = new FilePickerOpenOptions
            {
                Title = "Select FormID List File",
                FileTypeFilter = fileTypeChoices,
                AllowMultiple = false
            };

            var files = await _storageProvider.OpenFilePickerAsync(options);
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }
        catch (Exception ex)
        {
            _viewModel.AddErrorMessage($"Error selecting FormID list file: {ex.Message}");
            return null;
        }
    }
}