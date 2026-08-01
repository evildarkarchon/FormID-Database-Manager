using System.Collections.Generic;
using System.IO;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.TestUtilities.Builders;

/// <summary>
///     Declares the Game Installation layout a detection rule needs — master files, release-marker executables and
///     marker directories — as data rather than as a directory tree on disk.
/// </summary>
/// <remarks>
///     Detection reads the file system only through its probe, so a layout is all a test needs: the caller feeds the
///     built <see cref="GameDetectionLayout" /> to an in-memory probe and lets the real rules run. Nothing here touches
///     the file system, which is why the default game root deliberately names a place that does not exist.
///     <para>
///     The builder produces a layout rather than a probe because the probe port is <c>internal</c> to Core and this
///     assembly is not named in Core's <c>InternalsVisibleTo</c>.
///     </para>
/// </remarks>
public class GameDetectionBuilder
{
    /// <summary>
    ///     The Data-directory segment name, matching the one the module canonicalizes to.
    /// </summary>
    private const string DataDirectoryName = "Data";

    private readonly List<string> _markerDirectories = [];
    private readonly List<string> _pluginFiles = [];
    private readonly List<string> _rootFiles = [];
    private string _gameRoot = "C:\\NoSuchPlace\\TestGame";

    /// <summary>
    ///     Sets the game root the layout hangs off.
    /// </summary>
    /// <param name="path">A game root path; it need not exist, and normally should not.</param>
    /// <returns>This builder, for chaining.</returns>
    public GameDetectionBuilder WithGameRoot(string path)
    {
        _gameRoot = path;
        return this;
    }

    /// <summary>
    ///     Declares a Plugin file present in the layout's Data directory.
    /// </summary>
    /// <param name="fileName">A file name such as <c>Skyrim.esm</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public GameDetectionBuilder AddPlugin(string fileName)
    {
        _pluginFiles.Add(fileName);
        return this;
    }

    /// <summary>
    ///     Declares several Plugin files present in the layout's Data directory.
    /// </summary>
    /// <param name="fileNames">File names such as <c>Skyrim.esm</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public GameDetectionBuilder AddPlugins(params string[] fileNames)
    {
        _pluginFiles.AddRange(fileNames);
        return this;
    }

    /// <summary>
    ///     Declares a file present beside the Data directory, in the game root.
    /// </summary>
    /// <param name="fileName">A release marker such as <c>SkyrimSE.exe</c> or <c>goggame-1746476928.info</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public GameDetectionBuilder AddRootFile(string fileName)
    {
        _rootFiles.Add(fileName);
        return this;
    }

    /// <summary>
    ///     Declares a directory present in the game root.
    /// </summary>
    /// <param name="directoryName">A marker directory such as <c>gogscripts</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public GameDetectionBuilder AddRootDirectory(string directoryName)
    {
        _markerDirectories.Add(directoryName);
        return this;
    }

    /// <summary>
    ///     Builds the declared layout.
    /// </summary>
    /// <returns>An immutable layout with every declared path expanded to a full path.</returns>
    public GameDetectionLayout Build()
    {
        var dataDirectory = Path.Combine(_gameRoot, DataDirectoryName);
        var files = new List<string>();

        foreach (var pluginFile in _pluginFiles)
        {
            files.Add(Path.Combine(dataDirectory, pluginFile));
        }

        foreach (var rootFile in _rootFiles)
        {
            files.Add(Path.Combine(_gameRoot, rootFile));
        }

        var directories = new List<string> { dataDirectory };

        foreach (var markerDirectory in _markerDirectories)
        {
            directories.Add(Path.Combine(_gameRoot, markerDirectory));
        }

        return new GameDetectionLayout(_gameRoot, dataDirectory, files, directories);
    }
}

/// <summary>
///     A Game Installation layout expressed as paths: what a probe should report present.
/// </summary>
/// <remarks>
///     The expected release is deliberately not carried here. It is the assertion, and a layout that hands the test
///     back the answer it fed in asserts nothing — worse, a layout that must detect as no game at all would have to
///     carry a release it never means.
/// </remarks>
/// <param name="GameRoot">The game root the layout hangs off.</param>
/// <param name="DataDirectory">The Data directory beneath that root.</param>
/// <param name="Files">Every declared file, fully qualified.</param>
/// <param name="Directories">Every declared directory, fully qualified, including the Data directory.</param>
public sealed record GameDetectionLayout(
    string GameRoot,
    string DataDirectory,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Directories);
