using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;

namespace FormID_Database_Manager.TestUtilities;

/// <summary>
///     Shared detection of whether a Bethesda game is installed, used by the test-skip attributes
///     so the installation check stays consistent across attribute families.
/// </summary>
internal static class GameInstallationHelper
{
    /// <summary>
    ///     Returns <see langword="true"/> when the game environment can be constructed and its data
    ///     folder exists on disk; otherwise <see langword="false"/>.
    /// </summary>
    public static bool IsGameInstalled(GameRelease gameRelease)
    {
        try
        {
            // Try to get the game environment - this will fail if the game isn't installed
            var env = GameEnvironment.Typical.Construct(gameRelease);
            return Directory.Exists(env.DataFolderPath);
        }
        catch
        {
            // If we can't construct the environment, the game isn't installed
            return false;
        }
    }
}
