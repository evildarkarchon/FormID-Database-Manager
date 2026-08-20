using System.Collections.Generic;
using System.Linq;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;

namespace FormID_Database_Manager.Tests.Performance;

/// <summary>
///     Composes deterministic performance load orders behind production aggregate Plugin Ingestion.
/// </summary>
/// <remarks>
///     Prepared Game Load Orders observes each generated file when the run starts. Load scenarios therefore receive
///     ready cases while cancellation scenarios that deliberately name no files receive unavailable cases.
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
        var pluginIngestion = new PluginIngestion(new PreparedGameLoadOrders(pluginNames.ToArray()));
        return new ProcessingRunExecutor(pluginIngestion, new FormIdRecordStoreSessionOpener());
    }
}
