using System.Collections.Generic;
using System.Linq;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Performance;

/// <summary>
///     Supplies a deterministic Plugin load order for performance scenarios that generate their own Plugin files.
/// </summary>
internal sealed class StaticGameLoadOrderProvider(IEnumerable<string> pluginNames) : IGameLoadOrderProvider
{
    private readonly IReadOnlyList<string> _pluginNames = pluginNames.ToArray();

    /// <inheritdoc />
    public GameLoadOrderSnapshot BuildSnapshot(
        GameRelease gameRelease,
        string dataPath,
        bool includeMasterFlagsLookup = false)
    {
        return new GameLoadOrderSnapshot(_pluginNames);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetListedPluginNames(GameRelease gameRelease, string dataPath)
    {
        return _pluginNames;
    }
}

/// <summary>
///     Composes deterministic performance load orders behind production aggregate Plugin Ingestion.
/// </summary>
internal static class PerformanceProcessingRunFactory
{
    /// <summary>
    ///     Creates an executor whose Plugin Ingestion owns the supplied deterministic load-order adapter while Processing
    ///     Run retains production Store opening, optimization, and cleanup.
    /// </summary>
    /// <param name="pluginNames">The generated Plugin names in deterministic load-order sequence.</param>
    /// <returns>A Processing Run executor using the production aggregate and Store lifecycle seams.</returns>
    public static ProcessingRunExecutor Create(IEnumerable<string> pluginNames)
    {
        var pluginIngestion = new PluginIngestion(new StaticGameLoadOrderProvider(pluginNames));
        return new ProcessingRunExecutor(pluginIngestion, new FormIdRecordStoreSessionOpener());
    }
}
