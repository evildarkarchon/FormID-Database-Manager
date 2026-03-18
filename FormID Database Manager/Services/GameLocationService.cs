using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Installs;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Locates game installations using Mutagen's GameLocations API.
/// Wraps the static API behind an interface for testability.
/// </summary>
public class GameLocationService : IGameLocationService
{
    public List<string> GetGameFolders(GameRelease release)
    {
        try
        {
            return GameLocations.GetGameFolders(release)
                .Select(dp => dp.Path)
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            // Mutagen may throw if registry keys are missing, malformed,
            // or platform-specific store handlers fail.
            return [];
        }
    }
}
