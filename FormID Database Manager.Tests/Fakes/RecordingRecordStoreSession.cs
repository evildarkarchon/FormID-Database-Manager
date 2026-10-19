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
    // The session's text-row role is the same recording as the standalone text-row writer fake, so it delegates
    // rather than repeating the record-and-enumerate loop.
    private readonly RecordingFormIdTextRowWriter _textRowWriter = new();

    public int OptimizeCallCount { get; private set; }

    public CancellationToken? OptimizeCancellationToken { get; private set; }

    public bool Disposed { get; private set; }

    public Exception? OptimizeException { get; init; }

    public Exception? DisposeException { get; init; }

    public Action? OptimizeAction { get; init; }

    /// <summary>
    ///     The rows the text-row role consumed, in stream order, across every call.
    /// </summary>
    public List<FormIdTextRow> WrittenTextRows => _textRowWriter.Rows;

    /// <summary>
    ///     The Update Mode of the most recent text-row role call, or <see langword="null" /> if it was never called.
    /// </summary>
    public UpdateMode? WrittenTextRowsUpdateMode => _textRowWriter.UpdateMode;

    public Task<FormIdPluginWriteResult> WritePluginAsync(
        string pluginName,
        IEnumerable<FormIdRecord> records,
        UpdateMode updateMode,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FormIdPluginWriteResult(0));
    }

    /// <inheritdoc />
    public Task WriteTextRowsAsync(
        IAsyncEnumerable<FormIdTextRow> rows,
        UpdateMode updateMode,
        CancellationToken cancellationToken = default)
    {
        return _textRowWriter.WriteTextRowsAsync(rows, updateMode, cancellationToken);
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
