using System.Collections.Generic;
using System.Linq;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.TestUtilities.Builders;

/// <summary>
///     Supplies a deterministic Plugin load order to scenarios that generate their own Plugin files, so nothing has to
///     stand up a real game install for Plugin Ingestion to have a load order to consult.
/// </summary>
/// <remarks>
///     Public rather than internal because it now crosses an assembly boundary: it lives beside the Plugin fixture
///     generator its callers use, in the shared test utilities project.
/// </remarks>
public sealed class StaticGameLoadOrderProvider : IGameLoadOrderProvider
{
    private readonly GameLoadOrderSnapshot _snapshot;

    /// <summary>
    ///     Creates a provider returning a snapshot with default binary read parameters.
    /// </summary>
    /// <param name="pluginNames">The generated Plugin names in deterministic load-order sequence.</param>
    public StaticGameLoadOrderProvider(IEnumerable<string> pluginNames)
        : this(new GameLoadOrderSnapshot(pluginNames.ToArray()))
    {
    }

    /// <summary>
    ///     Creates a provider returning a caller-supplied snapshot, for fixtures whose game needs a master-flags
    ///     lookup to resolve the master they declare.
    /// </summary>
    /// <param name="snapshot">The snapshot every <see cref="BuildSnapshot" /> call returns.</param>
    public StaticGameLoadOrderProvider(GameLoadOrderSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    /// <inheritdoc />
    public GameLoadOrderSnapshot BuildSnapshot(
        GameRelease gameRelease,
        string dataPath,
        bool includeMasterFlagsLookup = false)
    {
        return _snapshot;
    }
}
