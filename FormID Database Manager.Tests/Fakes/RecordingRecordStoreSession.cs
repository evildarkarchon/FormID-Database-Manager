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
    public int OptimizeCallCount { get; private set; }

    public CancellationToken? OptimizeCancellationToken { get; private set; }

    public bool Disposed { get; private set; }

    public Exception? OptimizeException { get; init; }

    public Exception? DisposeException { get; init; }

    public Action? OptimizeAction { get; init; }

    /// <summary>
    ///     The rows the text-row role consumed, in stream order, across every call.
    /// </summary>
    public List<FormIdTextRow> WrittenTextRows { get; } = [];

    /// <summary>
    ///     The Update Mode of the most recent text-row role call, or <see langword="null" /> if it was never called.
    /// </summary>
    public UpdateMode? WrittenTextRowsUpdateMode { get; private set; }

    public Task<FormIdPluginWriteResult> WritePluginAsync(
        string pluginName,
        IEnumerable<FormIdRecord> records,
        UpdateMode updateMode,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FormIdPluginWriteResult(0));
    }

    /// <summary>
    ///     Consumes the stream exactly once, as the role's contract requires, recording each row and the Update Mode.
    /// </summary>
    /// <inheritdoc />
    public async Task WriteTextRowsAsync(
        IAsyncEnumerable<FormIdTextRow> rows,
        UpdateMode updateMode,
        CancellationToken cancellationToken = default)
    {
        WrittenTextRowsUpdateMode = updateMode;

        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            WrittenTextRows.Add(row);
        }
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
