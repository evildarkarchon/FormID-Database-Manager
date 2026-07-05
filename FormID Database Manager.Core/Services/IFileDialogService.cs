namespace FormID_Database_Manager.Services;

/// <summary>
/// Provides UI-neutral file and folder selection operations for the main application workflow.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Selects the game installation or data directory to scan for plugins.
    /// </summary>
    /// <returns>The picker outcome, distinguishing selection, cancellation, and platform failure.</returns>
    Task<FileDialogResult> SelectGameDirectory();

    /// <summary>
    /// Selects the SQLite database path used for FormID output.
    /// </summary>
    /// <returns>The picker outcome, distinguishing selection, cancellation, and platform failure.</returns>
    Task<FileDialogResult> SelectDatabaseFile();

    /// <summary>
    /// Selects an optional pipe-delimited FormID list file.
    /// </summary>
    /// <returns>The picker outcome, distinguishing selection, cancellation, and platform failure.</returns>
    Task<FileDialogResult> SelectFormIdListFile();
}
