#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     A text-row writer that consumes the stream it is handed exactly once, as the role's contract requires, and
///     records every row and the Update Mode, with no SQLite involved.
/// </summary>
/// <remarks>
///     Shared from the fakes folder (issue #110) so FormID Text Ingestion's tests can observe what reaches the Store
///     through the role alone.
/// </remarks>
internal sealed class RecordingFormIdTextRowWriter : IFormIdTextRowWriter
{
    /// <summary>
    ///     The rows consumed from the stream, in stream order, across every call.
    /// </summary>
    public List<FormIdTextRow> Rows { get; } = [];

    /// <summary>
    ///     The Update Mode of the most recent call, or <see langword="null" /> if the role was never called.
    /// </summary>
    public UpdateMode? UpdateMode { get; private set; }

    /// <summary>
    ///     How many times the role was called.
    /// </summary>
    public int CallCount { get; private set; }

    /// <summary>
    ///     Runs as each row is consumed, after it is recorded, so a test can interleave the row with the progress
    ///     reported around it or cancel at a known row.
    /// </summary>
    public Action<FormIdTextRow>? OnRow { get; init; }

    /// <inheritdoc />
    public async Task WriteTextRowsAsync(
        IAsyncEnumerable<FormIdTextRow> rows,
        UpdateMode updateMode,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        UpdateMode = updateMode;

        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            Rows.Add(row);
            OnRow?.Invoke(row);
        }
    }
}
