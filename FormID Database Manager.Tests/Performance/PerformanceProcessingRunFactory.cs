using System.Collections.Generic;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;

namespace FormID_Database_Manager.Tests.Performance;

/// <summary>
///     Composes deterministic performance load orders behind production aggregate Plugin Ingestion.
/// </summary>
/// <remarks>
///     The deterministic load-order provider this used to declare alongside the factory now lives in the shared test
///     utilities project as <see cref="StaticGameLoadOrderProvider" />, beside the Plugin fixture generator these
///     scenarios write their Plugins with. The factory stays here because its name and its choices are
///     performance-specific.
/// </remarks>
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
