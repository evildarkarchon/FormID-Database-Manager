namespace FormID_Database_Manager.Services;

/// <summary>
///     One parsed, valid row of a FormID text file, handed by FormID Text Ingestion to the FormID Record Store.
/// </summary>
/// <param name="Plugin">The trimmed Plugin value, in the casing of this row.</param>
/// <param name="FormId">The trimmed FormID value.</param>
/// <param name="Entry">The trimmed Entry value.</param>
internal readonly record struct FormIdTextRow(string Plugin, string FormId, string Entry);

/// <summary>
///     Consumer-owned FormID Record Store role for persisting the rows of one FormID text file during FormID Text
///     Ingestion.
/// </summary>
/// <remarks>
///     <para>
///         FormID Text Ingestion owns this role just as Plugin Ingestion owns <see cref="IPluginFormIdRecordWriter" />
///         (ADR-0001, 2026-10-18). The surrounding Processing Run owns the complete Store session and its lifetime;
///         FormID Text Ingestion borrows only this role and does not open, optimize, or dispose the Store.
///     </para>
///     <para>
///         Calls do not overlap. The supplied stream is enumerated exactly once during the operation and is not
///         retained after it completes. Rows are staged, then committed with one transaction per Plugin key matched
///         case-insensitively, appending or replacing according to the Update Mode. Stored Plugin values keep the
///         casing of each row.
///     </para>
/// </remarks>
internal interface IFormIdTextRowWriter
{
    /// <summary>
    ///     Stages every row of the lazy stream and commits each Plugin's rows in its own transaction.
    /// </summary>
    /// <param name="rows">The lazy parsed rows, enumerated exactly once before this operation completes.</param>
    /// <param name="updateMode">Whether to append rows or replace each mentioned Plugin's existing rows.</param>
    /// <param name="cancellationToken">Stops enumeration, staging, and any remaining Plugin commits.</param>
    /// <returns>A task that completes when every Plugin has been committed.</returns>
    /// <remarks>
    ///     Whether the stream fails, a commit fails, or cancellation is requested, staging is cleared before the
    ///     operation returns, so a reused Store never commits stale rows.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="updateMode" /> is not supported.</exception>
    /// <exception cref="Exception">
    ///     The stream itself fails, which propagates unchanged, or a Plugin commit fails. One Plugin's commit failure
    ///     rolls back that Plugin without stopping the others; a single failure is rethrown as is, and several are
    ///     aggregated in an <see cref="AggregateException" />.
    /// </exception>
    Task WriteTextRowsAsync(
        IAsyncEnumerable<FormIdTextRow> rows,
        UpdateMode updateMode,
        CancellationToken cancellationToken = default);
}
