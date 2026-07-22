using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Run-scoped adapter for the FormID Record Store behavior used by a Processing Run.
/// </summary>
internal interface IFormIdRecordStoreSession : IAsyncDisposable
{
    Task<FormIdPluginWriteResult> WritePluginAsync(
        string pluginName,
        IEnumerable<FormIdRecord> records,
        UpdateMode updateMode,
        CancellationToken cancellationToken = default);

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
