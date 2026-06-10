using System.Collections.Generic;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Abstraction over Mutagen's GameLocations for locating game installations.
/// </summary>
public interface IGameLocationService
{
    /// <summary>
    /// Returns all detected install directories for a game release.
    /// May perform registry and file system I/O — call off the UI thread.
    /// </summary>
    List<string> GetGameFolders(GameRelease release);
}
