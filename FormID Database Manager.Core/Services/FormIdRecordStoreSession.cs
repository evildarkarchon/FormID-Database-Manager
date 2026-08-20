using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Complete run-scoped FormID Record Store session owned by a Processing Run.
/// </summary>
internal interface IFormIdRecordStoreSession : IPluginFormIdRecordWriter, IAsyncDisposable
{
    Task<FormIdTextFileImportResult> ImportFormIdTextFileAsync(
        string formIdTextFilePath,
        UpdateMode updateMode,
        IProgress<FormIdStoreProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task OptimizeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Opens a run-scoped FormID Record Store adapter for Processing Run execution.
/// </summary>
internal interface IFormIdRecordStoreSessionOpener
{
    Task<IFormIdRecordStoreSession> OpenAsync(
        string databasePath,
        GameRelease gameRelease,
        CancellationToken cancellationToken = default);
}

internal sealed class FormIdRecordStoreSessionOpener : IFormIdRecordStoreSessionOpener
{
    public async Task<IFormIdRecordStoreSession> OpenAsync(
        string databasePath,
        GameRelease gameRelease,
        CancellationToken cancellationToken = default)
    {
        return await FormIdRecordStore.OpenAsync(databasePath, gameRelease, cancellationToken).ConfigureAwait(false);
    }
}
