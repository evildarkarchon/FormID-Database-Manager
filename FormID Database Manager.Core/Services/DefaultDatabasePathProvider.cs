using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

internal static class DefaultDatabasePathProvider
{
    private const string AppDataDirectoryName = "FormID Database Manager";
    private const string DatabaseDirectoryName = "Databases";

    /// <summary>
    /// Creates the default database path under the current user's local application data directory.
    /// </summary>
    /// <param name="gameRelease">The selected game release used to choose the safe database filename.</param>
    /// <returns>The full path to a generated database file whose containing directory already exists.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the local application data directory cannot be resolved.</exception>
    public static string CreateDefaultDatabasePath(GameRelease gameRelease)
    {
        var localApplicationDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localApplicationDataRoot))
        {
            throw new InvalidOperationException(
                "Could not resolve the local application data directory for the current user.");
        }

        return CreateDefaultDatabasePath(gameRelease, localApplicationDataRoot);
    }

    /// <summary>
    /// Creates the default database path under the supplied local application data root.
    /// </summary>
    /// <param name="gameRelease">The selected game release used to choose the safe database filename.</param>
    /// <param name="localApplicationDataRoot">The user-writable local application data root to contain generated databases.</param>
    /// <returns>The full path to a generated database file whose containing directory already exists.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="localApplicationDataRoot" /> is blank.</exception>
    internal static string CreateDefaultDatabasePath(GameRelease gameRelease, string localApplicationDataRoot)
    {
        if (string.IsNullOrWhiteSpace(localApplicationDataRoot))
        {
            throw new ArgumentException("A local application data root is required.", nameof(localApplicationDataRoot));
        }

        var databaseDirectory = Path.Combine(localApplicationDataRoot, AppDataDirectoryName, DatabaseDirectoryName);
        Directory.CreateDirectory(databaseDirectory);

        return Path.Combine(databaseDirectory, $"{GameReleaseHelper.GetSafeTableName(gameRelease)}.db");
    }
}
