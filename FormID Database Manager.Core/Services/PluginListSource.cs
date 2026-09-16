using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Identifies the GameRelease and canonical Data directory from which a Plugin List is discovered.
/// </summary>
internal sealed class PluginListSource : IEquatable<PluginListSource>
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private PluginListSource(GameRelease gameRelease, string dataDirectory)
    {
        GameRelease = gameRelease;
        DataDirectory = dataDirectory;
    }

    /// <summary>
    ///     Gets the GameRelease whose Plugin List is being discovered.
    /// </summary>
    public GameRelease GameRelease { get; }

    /// <summary>
    ///     Gets the canonical full Data-directory path without a terminal separator.
    /// </summary>
    public string DataDirectory { get; }

    /// <inheritdoc />
    public bool Equals(PluginListSource? other)
    {
        return other is not null &&
               GameRelease == other.GameRelease &&
               PathComparer.Equals(DataDirectory, other.DataDirectory);
    }

    /// <summary>
    ///     Creates a source from either a game root or its Data directory.
    /// </summary>
    /// <param name="gameRelease">The GameRelease whose load order applies.</param>
    /// <param name="gameDirectory">A game root or Data-directory path.</param>
    /// <returns>A normalized Plugin List Source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gameDirectory" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="gameDirectory" /> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="gameRelease" /> is not a Supported GameRelease. Rejection happens here, at the boundary
    ///     where the caller has context, rather than deep inside a table lookup (ADR-0003).
    /// </exception>
    public static PluginListSource Create(GameRelease gameRelease, string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        // Supported-ness, not enum-definedness: Mutagen defines releases this application cannot process, and the
        // message below has always claimed to be about the former.
        if (!SupportedGameReleases.IsSupported(gameRelease))
        {
            throw new ArgumentOutOfRangeException(nameof(gameRelease), gameRelease, "Unsupported GameRelease value.");
        }

        // Canonicalization is the single owner of the Data-path rule, so equivalent spellings stay one source.
        return new PluginListSource(gameRelease, GameInstallations.CanonicalizeDataDirectory(gameDirectory));
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is PluginListSource other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(GameRelease, PathComparer.GetHashCode(DataDirectory));
    }

    /// <summary>
    ///     Compares two sources using GameRelease and operating-system-appropriate canonical path equality.
    /// </summary>
    public static bool operator ==(PluginListSource? left, PluginListSource? right)
    {
        return Equals(left, right);
    }

    /// <summary>
    ///     Determines whether two sources identify different GameReleases or canonical Data directories.
    /// </summary>
    public static bool operator !=(PluginListSource? left, PluginListSource? right)
    {
        return !Equals(left, right);
    }
}
