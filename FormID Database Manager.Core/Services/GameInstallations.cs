namespace FormID_Database_Manager.Services;

/// <summary>
///     Resolves Game Installation facts: the canonical Data directory a selected directory means.
/// </summary>
/// <remarks>
///     Canonicalization is the single implementation of the Data-path rule — Plugin List Source, Plugin Ingestion and
///     detection all call it, so a fix applies everywhere rather than at one of three call sites. Detection and install
///     location join this module later (ADR-0002); the type is a concrete sealed class with no interface because
///     nothing about it varies in production.
/// </remarks>
internal sealed class GameInstallations
{
    /// <summary>
    ///     The Data-directory segment name, matched case-insensitively so filesystem casing cannot change the answer.
    /// </summary>
    private const string DataDirectoryName = "Data";

    /// <summary>
    ///     Canonicalizes a game root or Data directory into the Data directory used for Plugin lookup.
    /// </summary>
    /// <param name="gameDirectory">A game root or its Data directory, in any equivalent spelling.</param>
    /// <returns>
    ///     A fully qualified Data-directory path with dot segments resolved and no trailing separator. A game root
    ///     yields that root's Data directory; a Data directory yields itself.
    /// </returns>
    /// <remarks>
    ///     Pure and static: it performs no I/O and never consults the file system, so an absent directory canonicalizes
    ///     exactly like a present one. Relative input is resolved against the current directory.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="gameDirectory" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="gameDirectory" /> is empty, whitespace, or malformed.</exception>
    internal static string CanonicalizeDataDirectory(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        // Fully qualify and drop any terminal separator before classifying the input, so that a pasted
        // "…\Data\", mixed separators, and dot segments all reach the Data check in one spelling.
        var canonicalInput = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));

        return Path.GetFileName(canonicalInput).Equals(DataDirectoryName, StringComparison.OrdinalIgnoreCase)
            ? canonicalInput
            : Path.Combine(canonicalInput, DataDirectoryName);
    }
}
