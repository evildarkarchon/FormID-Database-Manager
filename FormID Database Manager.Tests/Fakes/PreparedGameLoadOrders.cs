using System.Collections.Immutable;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     Prepares selected Plugins from deterministic listings while producing production-compatible opaque read
///     capabilities for integration, generated-Plugin, and performance fixtures.
/// </summary>
internal sealed class PreparedGameLoadOrders
    : IGameLoadOrders
{
    private readonly bool _supplyGeneratedFixtureMasterStyles;
    private readonly IReadOnlyList<string> _listedPluginNames;

    /// <summary>
    ///     Creates deterministic Game Load Orders that observes actual fixture-file availability.
    /// </summary>
    /// <param name="listedPluginNames">The ordered listed Plugin names.</param>
    internal PreparedGameLoadOrders(IReadOnlyList<string> listedPluginNames)
        : this(listedPluginNames, supplyGeneratedFixtureMasterStyles: false)
    {
    }

    private PreparedGameLoadOrders(
        IReadOnlyList<string> listedPluginNames,
        bool supplyGeneratedFixtureMasterStyles)
    {
        _listedPluginNames = listedPluginNames ?? throw new ArgumentNullException(nameof(listedPluginNames));
        _supplyGeneratedFixtureMasterStyles = supplyGeneratedFixtureMasterStyles;
    }

    /// <summary>
    ///     Creates deterministic Game Load Orders for generated Plugins whose declared base master is intentionally
    ///     not written beside the fixture.
    /// </summary>
    /// <param name="listedPluginNames">The ordered generated Plugin names.</param>
    /// <returns>Game Load Orders that supplies the fixture builder's independently defined master styles.</returns>
    internal static PreparedGameLoadOrders ForGeneratedPlugins(IReadOnlyList<string> listedPluginNames)
    {
        return new PreparedGameLoadOrders(listedPluginNames, supplyGeneratedFixtureMasterStyles: true);
    }

    /// <inheritdoc />
    public Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public ImmutableArray<PreparedSelectedPlugin> PrepareSelectedPlugins(
        GameRelease gameRelease,
        string canonicalDataDirectory,
        IReadOnlyList<string> selectedPluginNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canonicalDataDirectory);
        ArgumentNullException.ThrowIfNull(selectedPluginNames);
        cancellationToken.ThrowIfCancellationRequested();

        var listedPluginFiles = _listedPluginNames
            .Select(pluginName =>
            {
                var resolvedPluginPath = Path.Combine(canonicalDataDirectory, pluginName);
                return new PluginFileObservation(pluginName, resolvedPluginPath, File.Exists(resolvedPluginPath));
            })
            .ToArray();
        var readCapability = CreateReadCapability(gameRelease, listedPluginFiles);
        var filesByName = listedPluginFiles.ToDictionary(
            pluginFile => pluginFile.PluginName,
            StringComparer.OrdinalIgnoreCase);

        var prepared = ImmutableArray.CreateBuilder<PreparedSelectedPlugin>(selectedPluginNames.Count);
        foreach (var selectedPluginName in selectedPluginNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!filesByName.TryGetValue(selectedPluginName, out var listedPluginFile))
            {
                prepared.Add(new SelectedPluginNotListed(selectedPluginName));
            }
            else if (!listedPluginFile.IsAvailable)
            {
                prepared.Add(new SelectedPluginFileUnavailable(
                    selectedPluginName,
                    listedPluginFile.ResolvedPluginPath));
            }
            else
            {
                prepared.Add(new SelectedPluginReady(
                    selectedPluginName,
                    listedPluginFile.ResolvedPluginPath,
                    readCapability));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return prepared.ToImmutable();
    }

    /// <summary>
    ///     Creates the matched production capability used by the production overlay adapter.
    /// </summary>
    /// <param name="gameRelease">The fixture's Supported GameRelease.</param>
    /// <param name="listedPluginFiles">The deterministic listed-file observations.</param>
    /// <returns>A capability branded for <see cref="MutagenPluginOverlayReader" />.</returns>
    private PluginReadCapability CreateReadCapability(
        GameRelease gameRelease,
        IReadOnlyList<PluginFileObservation> listedPluginFiles)
    {
        if (!_supplyGeneratedFixtureMasterStyles || gameRelease != GameRelease.Starfield)
        {
            return new GameLoadOrderEnvironment().PreparePluginReads(gameRelease, listedPluginFiles);
        }

        // Generated Starfield Plugins declare Starfield.esm without writing it. Supply the independently known style
        // so happy-path fixtures test overlay behavior; the missing-master fixture uses the production empty lookup.
        var readParameters = new BinaryReadParameters
        {
            MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(PluginFixture.MasterStylesFor(gameRelease))
        };
        return new PluginReadCapability(new MutagenPluginReadCapabilityPayload(gameRelease, readParameters));
    }
}
