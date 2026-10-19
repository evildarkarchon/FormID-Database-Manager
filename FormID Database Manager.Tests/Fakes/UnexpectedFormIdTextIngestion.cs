#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     FormID Text Ingestion for a Processing Run that must never reach it: every call is counted and fails loudly.
/// </summary>
/// <remarks>
///     Issue #110. The counterpart of <see cref="UnexpectedPluginIngestion" />, for selected-Plugin runs and for runs
///     that must end before either ingestion module is reached.
/// </remarks>
internal sealed class UnexpectedFormIdTextIngestion : IFormIdTextIngestion
{
    /// <summary>
    ///     How many times ingestion or planning was reached.
    /// </summary>
    public int CallCount { get; private set; }

    /// <inheritdoc />
    public Task<FormIdTextFileImportResult> IngestAsync(
        FormIdTextProcessingRunRequest request,
        IFormIdTextRowWriter recordStore,
        IProgress<FormIdTextIngestionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromException<FormIdTextFileImportResult>(
            new InvalidOperationException("FormID Text Ingestion was not expected for this Processing Run."));
    }

    /// <inheritdoc />
    public Task<FormIdTextRunPlan> PlanAsync(
        FormIdTextProcessingRunRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromException<FormIdTextRunPlan>(
            new InvalidOperationException("FormID text planning was not expected for this Processing Run."));
    }
}
