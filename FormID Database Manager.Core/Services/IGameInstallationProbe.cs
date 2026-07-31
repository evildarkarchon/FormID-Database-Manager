using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Everything <see cref="GameInstallations" /> reads from outside the process, behind one seam.
/// </summary>
/// <remarks>
///     This is the module's only substitution seam (ADR-0002), with exactly two adapters: a production adapter over
///     the real file system and Mutagen's install-location API, and an in-memory adapter used by tests. Parent-directory
///     resolution is deliberately absent — that is path arithmetic, not I/O, so the module does it itself. The name
///     avoids "Environment", which already names a Mutagen type and would misdirect readers.
/// </remarks>
internal interface IGameInstallationProbe
{
    /// <summary>
    ///     Reports whether a file exists at the given path.
    /// </summary>
    /// <param name="path">A fully qualified file path.</param>
    /// <returns><see langword="true" /> when the file exists and is reachable.</returns>
    bool FileExists(string path);

    /// <summary>
    ///     Reports whether a directory exists at the given path.
    /// </summary>
    /// <param name="path">A fully qualified directory path.</param>
    /// <returns><see langword="true" /> when the directory exists and is reachable.</returns>
    bool DirectoryExists(string path);

    /// <summary>
    ///     Reads the install records for a GameRelease.
    /// </summary>
    /// <param name="release">The GameRelease whose recorded install directories are requested.</param>
    /// <returns>
    ///     The recorded install directories that are present on disk, in record order, without case-insensitive
    ///     duplicates. Empty covers every way that can come out: nothing recorded, records pointing at directories
    ///     that are gone, and install-record state the adapter could not read at all.
    /// </returns>
    IReadOnlyList<string> GetInstalledDirectories(GameRelease release);
}
