using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

internal static class DefaultDatabasePathProvider
{
    private const string AppDataDirectoryName = "FormID Database Manager";
    private const string DatabaseDirectoryName = "Databases";

    /// <summary>
    ///     Computes the default database path under the supplied default database root.
    /// </summary>
    /// <param name="gameRelease">The selected game release used to choose the safe database filename.</param>
    /// <param name="defaultDatabaseRoot">
    ///     The user-writable root resolved once by production composition (the local application data directory), or
    ///     null or blank when it could not be resolved.
    /// </param>
    /// <returns>
    ///     The full path to a generated database file. Nothing is created on disk: the FormID Record Store creates the
    ///     database's parent directory when it opens (ADR-0001), so a refused run or a Dry Run leaves nothing behind.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="defaultDatabaseRoot" /> is null or blank, because no default location exists.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="gameRelease" /> is not a Supported GameRelease.
    /// </exception>
    internal static string CreateDefaultDatabasePath(GameRelease gameRelease, string? defaultDatabaseRoot)
    {
        if (string.IsNullOrWhiteSpace(defaultDatabaseRoot))
        {
            // An unresolvable root is an environment fact rather than a caller mistake, so it keeps the wording the
            // run reported before the root was injected instead of becoming an argument exception.
            throw new InvalidOperationException(
                "Could not resolve the local application data directory for the current user.");
        }

        var databaseDirectory = Path.Combine(defaultDatabaseRoot, AppDataDirectoryName, DatabaseDirectoryName);

        // The Store's own table name is the filename, so a database file and the table inside it cannot disagree.
        return Path.Combine(databaseDirectory, $"{SupportedGameReleases.ForRelease(gameRelease).TableName}.db");
    }
}
