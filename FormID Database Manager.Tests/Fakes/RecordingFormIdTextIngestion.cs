#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     FormID Text Ingestion that records what a run asked of it and answers with configured progress, counts, and
///     plans, without reading any file.
/// </summary>
/// <remarks>
///     Issue #110. The executor's FormID text tests run against this fake, so they assert dispatch, restatement, and
///     the run's Store lifecycle; parsing and file reading are FormID Text Ingestion's and are tested there.
/// </remarks>
internal sealed class RecordingFormIdTextIngestion : IFormIdTextIngestion
{
    /// <summary>
    ///     Every ingestion, in order, with the Store role and token it was handed.
    /// </summary>
    public List<(
        FormIdTextProcessingRunRequest Request,
        IFormIdTextRowWriter RecordStore,
        CancellationToken CancellationToken)> IngestCalls { get; } = [];

    /// <summary>
    ///     Every plan request, in order, with the token it was handed.
    /// </summary>
    public List<(FormIdTextProcessingRunRequest Request, CancellationToken CancellationToken)> PlanCalls { get; } =
        [];

    /// <summary>
    ///     The progress each ingestion reports, in order, before it runs <see cref="IngestAction" />.
    /// </summary>
    public IReadOnlyList<FormIdTextIngestionProgress> ProgressReports { get; init; } = [];

    /// <summary>
    ///     Runs after the progress is reported and before the counts are returned, so a test can cancel or record
    ///     ordering from inside ingestion.
    /// </summary>
    public Action? IngestAction { get; init; }

    /// <summary>
    ///     The counts every ingestion returns.
    /// </summary>
    public FormIdTextFileImportResult Result { get; init; }

    /// <summary>
    ///     The plan every planning call returns, or <see langword="null" /> to plan the request's path as absent.
    /// </summary>
    public FormIdTextRunPlan? Plan { get; init; }

    /// <inheritdoc />
    public Task<FormIdTextFileImportResult> IngestAsync(
        FormIdTextProcessingRunRequest request,
        IFormIdTextRowWriter recordStore,
        IProgress<FormIdTextIngestionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IngestCalls.Add((request, recordStore, cancellationToken));

        foreach (var report in ProgressReports)
        {
            progress?.Report(report);
        }

        IngestAction?.Invoke();

        return Task.FromResult(Result);
    }

    /// <inheritdoc />
    public Task<FormIdTextRunPlan> PlanAsync(
        FormIdTextProcessingRunRequest request,
        CancellationToken cancellationToken = default)
    {
        PlanCalls.Add((request, cancellationToken));
        return Task.FromResult(Plan ?? new FormIdTextRunPlan(request.FormIdListPath, null));
    }
}
