namespace FormID_Database_Manager.Services;

/// <summary>
///     Compatibility wrapper for FormID text file imports; production callers should use FormIdRecordStore directly.
/// </summary>
internal sealed class FormIdTextProcessor
{
    /// <summary>
    ///     Processes a FormID text file through the store-owned import behavior.
    /// </summary>
    /// <param name="formIdListPath">The file path of the FormID list to process.</param>
    /// <param name="recordStore">The run-scoped FormID Record Store used for importing.</param>
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

        await recordStore.ImportFormIdTextFileAsync(
                formIdListPath,
                updateMode ? UpdateMode.ReplacePluginRecords : UpdateMode.Append,
                progress is null ? null : new ProgressAdapter(progress),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class ProgressAdapter(IProgress<(string Message, double? Value)> inner) : IProgress<FormIdStoreProgress>
    {
        public void Report(FormIdStoreProgress value)
        {
            inner.Report((value.Message, value.Value));
        }
    }
}
