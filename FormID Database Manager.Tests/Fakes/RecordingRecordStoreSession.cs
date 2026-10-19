#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     A Store session that records what a run asked of it and answers with configured results and failures.
/// </summary>
/// <remarks>
///     Moved out of the executor tests (issue #105) so the Processing Run Control tests can share it.
/// </remarks>
internal sealed class RecordingRecordStoreSession : IFormIdRecordStoreSession
{
    public string? ImportedTextFilePath { get; private set; }

    public UpdateMode? ImportedTextFileUpdateMode { get; private set; }

    public int OptimizeCallCount { get; private set; }

    public CancellationToken? OptimizeCancellationToken { get; private set; }

    public bool Disposed { get; private set; }

    public Exception? OptimizeException { get; init; }

    public Exception? DisposeException { get; init; }

    public Action? ImportAction { get; init; }

    public Action? OptimizeAction { get; init; }

    public FormIdTextFileImportResult TextFileImportResult { get; init; }

    public IReadOnlyList<FormIdStoreProgress> TextFileProgressReports { get; init; } = [];

    public Task<FormIdPluginWriteResult> WritePluginAsync(
        string pluginName,
        IEnumerable<FormIdRecord> records,
        UpdateMode updateMode,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FormIdPluginWriteResult(0));
    }

    /// <inheritdoc />
    public Task<FormIdTextFileImportResult> ImportFormIdTextFileAsync(
        string formIdTextFilePath,
        UpdateMode updateMode,
        IProgress<FormIdStoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ImportedTextFilePath = formIdTextFilePath;
        ImportedTextFileUpdateMode = updateMode;

        foreach (var report in TextFileProgressReports)
        {
            progress?.Report(report);
        }

        ImportAction?.Invoke();

        return Task.FromResult(TextFileImportResult);
    }

    /// <inheritdoc />
    public Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        OptimizeCallCount++;
        OptimizeCancellationToken = cancellationToken;
        OptimizeAction?.Invoke();
        return OptimizeException is null
            ? Task.CompletedTask
            : Task.FromException(OptimizeException);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return DisposeException is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(DisposeException);
    }
}
