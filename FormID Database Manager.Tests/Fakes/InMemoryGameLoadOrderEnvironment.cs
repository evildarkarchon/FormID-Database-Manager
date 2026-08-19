using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     The deterministic in-memory adapter for the lower Game Load Order environment seam.
/// </summary>
internal sealed class InMemoryGameLoadOrderEnvironment : IGameLoadOrderEnvironment
{
    private readonly HashSet<string> _availablePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(GameRelease Release, string DataDirectory), IReadOnlyList<string>> _loadOrders = [];
    private readonly List<(GameRelease Release, string DataDirectory)> _readRequests = [];
    private readonly List<string> _observedPluginPaths = [];

    /// <summary>
    ///     Runs during each Game Load Order read, after the request is recorded.
    /// </summary>
    public Action? BeforeRead { get; set; }

    /// <summary>
    ///     Runs during each Plugin-file observation, after its resolved path is recorded.
    /// </summary>
    public Action<string>? BeforeFileObservation { get; set; }

    /// <summary>
    ///     The failure raised by Game Load Order reading, when one is configured.
    /// </summary>
    public Exception? ReadFailure { get; set; }

    /// <summary>
    ///     The failure raised by Plugin-file observation, when one is configured.
    /// </summary>
    public Exception? FileObservationFailure { get; set; }

    /// <summary>
    ///     The Game Load Order reads performed by the module.
    /// </summary>
    public IReadOnlyList<(GameRelease Release, string DataDirectory)> ReadRequests => _readRequests;

    /// <summary>
    ///     The resolved Plugin paths observed by the module, in order.
    /// </summary>
    public IReadOnlyList<string> ObservedPluginPaths => _observedPluginPaths;

    /// <summary>
    ///     The number of eager shared Plugin-read preparations performed.
    /// </summary>
    public int PreparationCount { get; private set; }

    /// <summary>
    ///     The complete listed Plugin-file facts supplied to the most recent preparation.
    /// </summary>
    public IReadOnlyList<PluginFileObservation> PreparedPluginFiles { get; private set; } = [];

    /// <summary>
    ///     The most recently prepared opaque capability.
    /// </summary>
    public PluginReadCapability? PreparedCapability { get; private set; }

    /// <inheritdoc />
    public GameLoadOrder ReadGameLoadOrder(GameRelease gameRelease, string canonicalDataDirectory)
    {
        var normalizedDataDirectory = Normalize(canonicalDataDirectory);
        _readRequests.Add((gameRelease, normalizedDataDirectory));
        BeforeRead?.Invoke();
        if (ReadFailure is not null)
        {
            throw ReadFailure;
        }

        var key = (gameRelease, normalizedDataDirectory);
        var pluginNames = _loadOrders.TryGetValue(key, out var configured) ? configured : [];
        return new GameLoadOrder(gameRelease, pluginNames.Select(static name => new GameLoadOrderListing(name)));
    }

    /// <inheritdoc />
    public PluginFileObservation ObservePluginFile(
        string canonicalDataDirectory,
        GameLoadOrderListing listing)
    {
        var resolvedPath = Path.Combine(canonicalDataDirectory, listing.PluginName);
        _observedPluginPaths.Add(resolvedPath);
        BeforeFileObservation?.Invoke(resolvedPath);
        if (FileObservationFailure is not null)
        {
            throw FileObservationFailure;
        }

        return new PluginFileObservation(
            listing.PluginName,
            resolvedPath,
            _availablePaths.Contains(Normalize(resolvedPath)));
    }

    /// <inheritdoc />
    public PluginReadCapability PreparePluginReads(
        GameRelease gameRelease,
        IReadOnlyList<PluginFileObservation> listedPluginFiles)
    {
        PreparationCount++;
        PreparedPluginFiles = listedPluginFiles.ToArray();
        PreparedCapability = new PluginReadCapability(new InMemoryPluginReadCapabilityPayload());
        return PreparedCapability;
    }

    /// <summary>
    ///     Declares one ordered Game Load Order for a GameRelease and canonical Data directory.
    /// </summary>
    /// <param name="gameRelease">The GameRelease that owns the Game Load Order.</param>
    /// <param name="canonicalDataDirectory">The canonical Data directory.</param>
    /// <param name="pluginNames">The listed Plugin names, in Game Load Order order.</param>
    /// <returns>This adapter, for chaining.</returns>
    public InMemoryGameLoadOrderEnvironment WithLoadOrder(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        params string[] pluginNames)
    {
        _loadOrders[(gameRelease, Normalize(canonicalDataDirectory))] = pluginNames;
        return this;
    }

    /// <summary>
    ///     Declares one Plugin file as available under a canonical Data directory.
    /// </summary>
    /// <param name="canonicalDataDirectory">The canonical Data directory.</param>
    /// <param name="pluginName">The Plugin filename.</param>
    /// <returns>This adapter, for chaining.</returns>
    public InMemoryGameLoadOrderEnvironment WithAvailablePlugin(
        string canonicalDataDirectory,
        string pluginName)
    {
        _availablePaths.Add(Normalize(Path.Combine(canonicalDataDirectory, pluginName)));
        return this;
    }

    private static string Normalize(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>
    ///     Brands capabilities created by this in-memory adapter so production consumers reject them deterministically.
    /// </summary>
    private sealed class InMemoryPluginReadCapabilityPayload
    {
    }
}
