using System.Text;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Owns reading one FormID text file for a Processing Run: its Dry Run plan, its parsing, its counts, and its
///     import progress.
/// </summary>
/// <remarks>
///     It borrows only the text-row writer role of the run's Store session and never opens, optimizes, or disposes the
///     Store (ADR-0001, 2026-10-18). It reports facts, never words: how they are worded, and whether a Plugin is named
///     at all, are the Processing Run's and Processing Run Presentation's decisions (ADR-0007).
/// </remarks>
internal sealed class FormIdTextIngestion : IFormIdTextIngestion
{
    /// <summary>
    ///     The large read buffer that keeps reading big files fast; the stream position it advances is the bytes read.
    /// </summary>
    private const int ReadBufferSize = 81920;

    /// <summary>
    ///     How many valid rows separate two periodic counter updates.
    /// </summary>
    private const int ProgressInterval = 1000;

    /// <inheritdoc />
    public async Task<FormIdTextFileImportResult> IngestAsync(
        FormIdTextProcessingRunRequest request,
        IFormIdTextRowWriter recordStore,
        IProgress<FormIdTextIngestionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(recordStore);

        var path = request.FormIdListPath;
        var processedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recordCount = 0;

        // Use stream position for progress tracking; it avoids per-line UTF-8 byte counting on large files. Length is
        // read before the file is opened, so a missing file fails here, before anything is reported or written.
        var totalBytes = new FileInfo(path).Length;

        progress?.Report(new FormIdTextIngestionOpening(totalBytes));

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // The role consumes the stream once and clears its staging however it ends, so no cleanup is needed here.
        await recordStore.WriteTextRowsAsync(ReadValidRowsAsync(), request.UpdateMode, cancellationToken)
            .ConfigureAwait(false);

        return new FormIdTextFileImportResult(processedPlugins.Count, recordCount);

        // Counts and progress are updated as each row is read, before it is yielded, so every report precedes the
        // Store receiving the row it describes.
        async IAsyncEnumerable<FormIdTextRow> ReadValidRowsAsync()
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                var bytesRead = stream.Position;

                if (!TryParseLine(line, out var row))
                {
                    continue;
                }

                recordCount++;

                if (recordCount % ProgressInterval == 0)
                {
                    // A coincident first encounter follows this report, preserving the order the user sees.
                    progress?.Report(new FormIdTextIngestionCounterUpdate(recordCount, bytesRead, totalBytes));
                }

                if (processedPlugins.Add(row.Plugin))
                {
                    // Report the fact in both modes: whether to show a Plugin announcement belongs to the run.
                    progress?.Report(new FormIdTextIngestionPluginFirstEncountered(
                        recordCount,
                        bytesRead,
                        totalBytes,
                        row.Plugin));
                }

                yield return row;
            }
        }
    }

    /// <inheritdoc />
    public Task<FormIdTextRunPlan> PlanAsync(
        FormIdTextProcessingRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<FormIdTextRunPlan>(cancellationToken);
        }

        var formIdListFile = new FileInfo(request.FormIdListPath);

        // Exists is read before Length because Length throws for a file that is not there, and absence is a fact this
        // plan reports rather than a failure it raises.
        return Task.FromResult(new FormIdTextRunPlan(
            request.FormIdListPath,
            formIdListFile.Exists ? formIdListFile.Length : null));
    }

    /// <summary>
    ///     Parses one line of a FormID text file.
    /// </summary>
    /// <param name="line">The raw line, without its terminator.</param>
    /// <param name="row">The trimmed Plugin, FormID, and Entry when the line is valid.</param>
    /// <returns>
    ///     <see langword="true" /> when the line has exactly two pipes; blank lines and any other shape are malformed
    ///     and are skipped by the caller rather than failing the run.
    /// </returns>
    private static bool TryParseLine(string line, out FormIdTextRow row)
    {
        row = default;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var span = line.AsSpan();
        var firstPipe = span.IndexOf('|');
        if (firstPipe < 0)
        {
            return false;
        }

        var rest = span[(firstPipe + 1)..];
        var secondPipe = rest.IndexOf('|');
        if (secondPipe < 0)
        {
            return false;
        }

        var afterSecond = rest[(secondPipe + 1)..];
        if (afterSecond.IndexOf('|') >= 0)
        {
            return false;
        }

        row = new FormIdTextRow(
            span[..firstPipe].Trim().ToString(),
            rest[..secondPipe].Trim().ToString(),
            afterSecond.Trim().ToString());
        return true;
    }
}
