using System.Text;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Provides functionality for processing FormID list files and managing their insertion into a SQLite database.
///     This class is designed to handle parsing and batching of FormID records for efficient database operations.
/// </summary>
public class FormIdTextProcessor
{
    private const int UiUpdateInterval = 1000; // Update UI every 1000 records

    /// <summary>
    ///     Processes a FormID list file by reading records, organizing them by the associated plugin,
    ///     and batching them for insertion into a SQLite database.
    /// </summary>
    /// <param name="formIdListPath">The file path of the FormID list to process.</param>
    /// <param name="recordStore">The run-scoped FormID Record Store used for staged writes.</param>
    /// <param name="updateMode">Indicates whether the operation is performed in update mode or not.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <param name="progress">Optional progress reporter for updating task details and progress percentage.</param>
    /// <returns>A task that represents the asynchronous operation of processing the FormID list file.</returns>
    public async Task ProcessFormIdListFile(
        string formIdListPath,
        FormIdRecordStore recordStore,
        bool updateMode,
        CancellationToken cancellationToken,
        IProgress<(string Message, double? Value)>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(recordStore);

        var processedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recordCount = 0;

        // Use stream position for progress tracking (avoids per-line UTF8.GetByteCount overhead)
        var fileInfo = new FileInfo(formIdListPath);
        var totalBytes = fileInfo.Length;

        progress?.Report(("Starting processing...", 0));

        // Read the file line by line with byte-based progress tracking
        await using var stream = new FileStream(formIdListPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                break;
            }

            // Use stream position directly for progress (no per-line byte counting)
            var bytesRead = stream.Position;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Span-based parsing: find pipe positions without allocating Split array
            var span = line.AsSpan();
            var firstPipe = span.IndexOf('|');
            if (firstPipe < 0)
            {
                continue;
            }

            var rest = span[(firstPipe + 1)..];
            var secondPipe = rest.IndexOf('|');
            if (secondPipe < 0)
            {
                continue;
            }

            // Check for extra pipes (more than 3 parts)
            var afterSecond = rest[(secondPipe + 1)..];
            if (afterSecond.IndexOf('|') >= 0)
            {
                continue;
            }

            var pluginName = span[..firstPipe].Trim().ToString();
            var formId = rest[..secondPipe].Trim().ToString();
            var entry = afterSecond.Trim().ToString();

            recordCount++;

            // Update progress periodically (Progress<T> already marshals to UI thread)
            if (recordCount % UiUpdateInterval == 0)
            {
                var progressPercent = totalBytes > 0 ? (double)bytesRead / totalBytes * 100 : 0;
                progress?.Report(($"Processing: {progressPercent:F1}% ({recordCount:N0} records)",
                    progressPercent));
            }

            if (!processedPlugins.Contains(pluginName))
            {
                if (updateMode)
                {
                    progress?.Report(($"Processing plugin: {pluginName}", null));
                }

                processedPlugins.Add(pluginName);
            }

            await recordStore.StageTextRecordAsync(pluginName, formId, entry, cancellationToken).ConfigureAwait(false);
        }

        await recordStore.CommitStagedTextRecordsAsync(updateMode, cancellationToken).ConfigureAwait(false);

        progress?.Report(($"Completed processing {processedPlugins.Count} plugins ({recordCount:N0} total records)",
            100));
    }
}
