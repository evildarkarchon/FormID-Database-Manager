#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     The in-memory <see cref="IGameInstallationProbe" />: a declared layout instead of a directory tree on disk.
/// </summary>
/// <remarks>
///     This is the second of the seam's two adapters (ADR-0002). Because <c>GameInstallations</c> has no interface,
///     tests substitute this probe and let the real detection rules run against the layout it declares.
///     <para>
///     It lives in the test project rather than beside the other shared fakes in TestUtilities because
///     <c>IGameInstallationProbe</c> is <c>internal</c> to Core and only the test and WinUI projects are named in
///     <c>InternalsVisibleTo</c> — TestUtilities cannot see the interface it would have to implement.
///     </para>
/// </remarks>
internal sealed class InMemoryGameInstallationProbe : IGameInstallationProbe
{
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GameRelease, IReadOnlyList<string>> _installedDirectories = [];
    private readonly List<string> _probedPaths = [];

    /// <summary>
    ///     Runs before each file or directory probe, so a test can observe or gate detection as it happens.
    /// </summary>
    public Action<string>? BeforeProbe { get; set; }

    /// <summary>
    ///     The paths detection probed, in probe order.
    /// </summary>
    public IReadOnlyList<string> ProbedPaths => _probedPaths;

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        RecordProbe(path);
        return _files.Contains(Normalize(path));
    }

    /// <inheritdoc />
    public bool DirectoryExists(string path)
    {
        RecordProbe(path);
        return _directories.Contains(Normalize(path));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetInstalledDirectories(GameRelease release)
    {
        return _installedDirectories.TryGetValue(release, out var directories) ? directories : [];
    }

    /// <summary>
    ///     Declares a file, and every directory above it, as present.
    /// </summary>
    /// <param name="path">The file path, in any equivalent spelling.</param>
    /// <returns>This probe, for chaining.</returns>
    public InMemoryGameInstallationProbe WithFile(string path)
    {
        var normalized = Normalize(path);
        _files.Add(normalized);
        AddAncestorDirectories(normalized);
        return this;
    }

    /// <summary>
    ///     Declares a directory, and every directory above it, as present.
    /// </summary>
    /// <param name="path">The directory path, in any equivalent spelling.</param>
    /// <returns>This probe, for chaining.</returns>
    public InMemoryGameInstallationProbe WithDirectory(string path)
    {
        var normalized = Normalize(path);
        _directories.Add(normalized);
        AddAncestorDirectories(normalized);
        return this;
    }

    /// <summary>
    ///     Declares every file and directory of a built layout as present.
    /// </summary>
    /// <param name="layout">A layout built by <c>GameDetectionBuilder</c>.</param>
    /// <returns>This probe, for chaining.</returns>
    public InMemoryGameInstallationProbe WithLayout(GameDetectionLayout layout)
    {
        foreach (var directory in layout.Directories)
        {
            WithDirectory(directory);
        }

        foreach (var file in layout.Files)
        {
            WithFile(file);
        }

        return this;
    }

    /// <summary>
    ///     Declares the install records a GameRelease lookup returns.
    /// </summary>
    /// <param name="release">The GameRelease being recorded.</param>
    /// <param name="directories">The recorded install directories, in record order.</param>
    /// <returns>This probe, for chaining.</returns>
    public InMemoryGameInstallationProbe WithInstalledDirectories(GameRelease release, params string[] directories)
    {
        _installedDirectories[release] = directories;
        return this;
    }

    /// <summary>
    ///     Reports whether anything at or below a directory was probed.
    /// </summary>
    /// <param name="directory">The directory whose subtree is in question.</param>
    /// <returns><see langword="true" /> when at least one probe fell inside that subtree.</returns>
    public bool ProbedAnythingUnder(string directory)
    {
        // The separator matters: "C:\Games\Skyrim" must not match a probe of "C:\Games\SkyrimVR".
        var subtreePrefix = Normalize(directory) + Path.DirectorySeparatorChar;
        return _probedPaths.Any(path =>
        {
            var normalized = Normalize(path);
            return normalized.Equals(Normalize(directory), StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(subtreePrefix, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void RecordProbe(string path)
    {
        _probedPaths.Add(path);
        BeforeProbe?.Invoke(path);
    }

    private void AddAncestorDirectories(string normalizedPath)
    {
        for (var parent = Path.GetDirectoryName(normalizedPath);
             !string.IsNullOrEmpty(parent);
             parent = Path.GetDirectoryName(parent))
        {
            // Stops at the root, whose GetDirectoryName is null, so a declared file implies its whole chain.
            if (!_directories.Add(parent))
            {
                return;
            }
        }
    }

    /// <summary>
    ///     Reduces a path to the one spelling the layout is keyed by, matching how the real file system treats
    ///     equivalent spellings.
    /// </summary>
    private static string Normalize(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
