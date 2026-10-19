#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     Opens the one Store session it was given and records every open, so a test can prove whether a run opened a
///     FormID Record Store at all.
/// </summary>
/// <param name="recordStore">The session returned by every open.</param>
/// <remarks>
///     Moved out of the executor tests (issue #105) so the Processing Run Control tests can share it.
/// </remarks>
internal sealed class RecordingRecordStoreSessionOpener(IFormIdRecordStoreSession recordStore)
    : IFormIdRecordStoreSessionOpener
{
    /// <summary>
    ///     Every open, in order.
    /// </summary>
    public List<(
        string DatabasePath,
        GameRelease GameRelease,
        CancellationToken CancellationToken)> OpenCalls { get; } = [];

    /// <inheritdoc />
    public Task<IFormIdRecordStoreSession> OpenAsync(
        string databasePath,
        GameRelease gameRelease,
        CancellationToken cancellationToken = default)
    {
        OpenCalls.Add((databasePath, gameRelease, cancellationToken));
        return Task.FromResult(recordStore);
    }
}
