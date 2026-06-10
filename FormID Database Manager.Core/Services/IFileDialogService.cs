using System.Threading.Tasks;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Provides UI-neutral file and folder selection operations for the main application workflow.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Selects the game installation or data directory to scan for plugins.
    /// </summary>
    /// <returns>The selected directory path, or <see langword="null"/> when the dialog is canceled or fails.</returns>
    Task<string?> SelectGameDirectory();

    /// <summary>
    /// Selects the SQLite database path used for FormID output.
    /// </summary>
    /// <returns>The selected database path, or <see langword="null"/> when the dialog is canceled or fails.</returns>
    Task<string?> SelectDatabaseFile();

    /// <summary>
    /// Selects an optional pipe-delimited FormID list file.
    /// </summary>
    /// <returns>The selected text file path, or <see langword="null"/> when the dialog is canceled or fails.</returns>
    Task<string?> SelectFormIdListFile();
}
