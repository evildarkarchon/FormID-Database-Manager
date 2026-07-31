using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Resolves Game Installation facts: the canonical Data directory a selected directory means, and which
///     GameRelease is installed at a directory.
/// </summary>
/// <remarks>
///     Canonicalization is the single implementation of the Data-path rule — Plugin List Source, Plugin Ingestion and
///     detection all call it, so a fix applies everywhere rather than at one of three call sites. Install location
///     joins this module later (ADR-0002); the type is a concrete sealed class with no interface and no
///     <c>virtual</c> members because nothing about it varies in production — tests get their control from
///     <see cref="IGameInstallationProbe" />. It is synchronous: thread placement is the caller's decision.
///     <para>
///     Base game Plugins are not here: they are a constant table in <c>BaseGamePlugins</c>, read directly by the
///     Plugin List, so a change to detection cannot affect Plugin List membership (ADR-0002).
///     </para>
/// </remarks>
internal sealed class GameInstallations
{
    /// <summary>
    ///     The Data-directory segment name, matched case-insensitively so filesystem casing cannot change the answer.
    /// </summary>
    private const string DataDirectoryName = "Data";

    private readonly IGameInstallationProbe _probe;

    /// <summary>
    ///     Creates a resolution module reading the outside world through one probe.
    /// </summary>
    /// <param name="probe">The module's only substitution seam.</param>
    /// <exception cref="ArgumentNullException"><paramref name="probe" /> is null.</exception>
    internal GameInstallations(IGameInstallationProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

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

    /// <summary>
    ///     Detects which GameRelease is installed at a directory, by the master files and release markers present.
    /// </summary>
    /// <param name="gameDirectory">A game root or its Data directory, in any equivalent spelling.</param>
    /// <returns>The detected GameRelease, or null when no known game master file was found.</returns>
    /// <remarks>
    ///     Null means exactly one thing: no known game master file was found. A path that is malformed rather than
    ///     merely game-less throws, so the caller can tell the user which of the two actually happened (ADR-0002).
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="gameDirectory" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="gameDirectory" /> is empty, whitespace, or malformed.</exception>
    internal GameRelease? Detect(string gameDirectory)
    {
        // One canonical Data directory covers both accepted inputs, so a game root and its Data directory —
        // however either is spelled — probe the same place. The game root is then path arithmetic on that result,
        // which is why it is not part of the probe.
        var dataDirectory = CanonicalizeDataDirectory(gameDirectory);
        var gameRoot = Path.GetDirectoryName(dataDirectory) ?? dataDirectory;

        if (_probe.FileExists(Path.Combine(dataDirectory, "Skyrim.esm")))
        {
            return DetectSkyrimRelease(dataDirectory, gameRoot);
        }

        if (_probe.FileExists(Path.Combine(dataDirectory, "Oblivion.esm")))
        {
            return GameRelease.Oblivion;
        }

        if (_probe.FileExists(Path.Combine(dataDirectory, "Fallout4.esm")))
        {
            return _probe.FileExists(Path.Combine(gameRoot, "Fallout4VR.exe"))
                ? GameRelease.Fallout4VR
                : GameRelease.Fallout4;
        }

        if (_probe.FileExists(Path.Combine(dataDirectory, "Starfield.esm")))
        {
            return GameRelease.Starfield;
        }

        return null;
    }

    /// <summary>
    ///     Distinguishes the releases that share Skyrim.esm, by the executables and store markers beside the Data
    ///     directory.
    /// </summary>
    /// <param name="dataDirectory">The canonical Data directory holding Skyrim.esm.</param>
    /// <param name="gameRoot">The directory above it, where release markers live.</param>
    /// <returns>The specific Skyrim or Enderal release; Legendary Edition when nothing narrows it further.</returns>
    private GameRelease DetectSkyrimRelease(string dataDirectory, string gameRoot)
    {
        var hasSkyrimSeExecutable = _probe.FileExists(Path.Combine(gameRoot, "SkyrimSE.exe"));

        if (_probe.FileExists(Path.Combine(dataDirectory, "Enderal - Forgotten Stories.esm")))
        {
            if (hasSkyrimSeExecutable)
            {
                return GameRelease.EnderalSE;
            }

            if (_probe.FileExists(Path.Combine(gameRoot, "TESV.exe")))
            {
                return GameRelease.EnderalLE;
            }
        }

        if (_probe.DirectoryExists(Path.Combine(gameRoot, "gogscripts")) ||
            _probe.FileExists(Path.Combine(gameRoot, "goggame-1746476928.info")))
        {
            return GameRelease.SkyrimSEGog;
        }

        if (_probe.FileExists(Path.Combine(gameRoot, "SkyrimVR.exe")))
        {
            return GameRelease.SkyrimVR;
        }

        return hasSkyrimSeExecutable ? GameRelease.SkyrimSE : GameRelease.SkyrimLE;
    }
}
