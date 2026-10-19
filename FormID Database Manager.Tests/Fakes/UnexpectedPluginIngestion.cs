#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     Plugin Ingestion for a Processing Run that must never reach it: every call is counted and fails loudly.
/// </summary>
/// <remarks>
///     Moved out of the executor tests (issue #105) so the Processing Run Control tests can run the real executor over
///     it and show a run cancelled during startup never reaches ingestion.
/// </remarks>
internal sealed class UnexpectedPluginIngestion : IPluginIngestion
{
    /// <summary>
    ///     How many times ingestion or planning was reached.
    /// </summary>
    public int CallCount { get; private set; }

    /// <inheritdoc />
    public Task<PluginIngestionReport> IngestAsync(
        PluginProcessingRunRequest request,
        IPluginFormIdRecordWriter recordStore,
        IProgress<PluginIngestionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromException<PluginIngestionReport>(
            new InvalidOperationException("Plugin Ingestion was not expected for this Processing Run."));
    }

    /// <inheritdoc />
    public Task<PluginIngestionPlan> PlanAsync(
        PluginProcessingRunRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromException<PluginIngestionPlan>(
            new InvalidOperationException("Planning was not expected for this Processing Run."));
    }
}
