using Mutagen.Bethesda;
using Mutagen.Bethesda.Installs;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The production <see cref="IGameInstallationProbe" />: the real file system plus Mutagen's install-location API.
/// </summary>
internal sealed class GameInstallationProbe : IGameInstallationProbe
{
    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="File.Exists(string)" /> reports false rather than throwing when a path is unreachable, so an
    ///     inaccessible directory reads as "no such file" rather than as a failure.
    /// </remarks>
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="Directory.Exists(string)" /> reports false rather than throwing when a path is unreachable.
    /// </remarks>
    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The catch-all here is adapter behaviour, not module behaviour (ADR-0002): Mutagen's lookup genuinely throws
    ///     on missing or malformed registry state and on platform-specific store handler failures, and an unusual system
    ///     configuration must read as "nothing recorded" rather than stopping the user working.
    /// </remarks>
    public IReadOnlyList<string> GetInstalledDirectories(GameRelease release)
    {
        try
        {
            return GameLocations.GetGameFolders(release)
                .Select(directory => directory.Path)
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
