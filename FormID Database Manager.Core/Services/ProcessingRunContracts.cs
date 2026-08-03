using System.Collections.Immutable;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The validation failure raised when a Processing Run cannot be started from the supplied domain request.
/// </summary>
public sealed class ProcessingRunValidationException : Exception
{
    /// <summary>
    ///     Creates a validation failure with a user-facing message.
    /// </summary>
    /// <param name="message">The reason the Processing Run cannot start.</param>
    public ProcessingRunValidationException(string message) : base(message)
    {
    }
}

/// <summary>
///     The failure raised when a selected Plugin declares a master the resolved Data directory cannot supply, on a
///     GameRelease whose load order separates master files by type.
/// </summary>
/// <remarks>
///     This fails the whole Processing Run rather than one Plugin, because it is a fact about the Data directory and
///     not about the Plugin: every spec-correct Plugin declares its game's main master first, so every selected Plugin
///     would fail identically. Reporting it as a Failed Plugin would name the wrong thing (ADR-0006, issue #52).
/// </remarks>
public sealed class UnresolvableMasterException : Exception
{
    /// <summary>
    ///     Creates the run-level failure for one unresolvable master.
    /// </summary>
    /// <param name="pluginName">The selected Plugin whose declared master could not be resolved.</param>
    /// <param name="masterName">
    ///     The declared master file name, or <see langword="null" /> when no master-flags lookup was supplied at all and
    ///     the underlying failure therefore names no individual master.
    /// </param>
    /// <param name="innerException">The underlying master-resolution failure, retained for diagnostics.</param>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is blank.</exception>
    public UnresolvableMasterException(string pluginName, string? masterName, Exception? innerException = null)
        // Validated inside the base initializer rather than in the body, so a blank name cannot reach message
        // construction — a constructor body runs only after base(), which is too late to guard it.
        : base(BuildMessage(EnsurePluginName(pluginName), masterName), innerException)
    {
        PluginName = pluginName;
        MasterName = masterName;
    }

    /// <summary>
    ///     The selected Plugin whose declared master could not be resolved.
    /// </summary>
    public string PluginName { get; }

    /// <summary>
    ///     The unresolvable master file name, or <see langword="null" /> when the underlying failure named none.
    /// </summary>
    public string? MasterName { get; }

    /// <summary>
    ///     Validates the Plugin name and returns it, so the guard can run inside the base initializer.
    /// </summary>
    /// <param name="pluginName">The selected Plugin name.</param>
    /// <returns>The same Plugin name.</returns>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is blank.</exception>
    private static string EnsurePluginName(string pluginName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        return pluginName;
    }

    private static string BuildMessage(string pluginName, string? masterName)
    {
        // The wording stays on the Data directory rather than the Plugin, because pointing the user at the Plugin
        // would send them to fix something that is not broken.
        return masterName is null
            ? $"Could not resolve the master files declared by {pluginName}. This game separates master files by " +
              "type in the load order, but no master file was found in the Data directory being processed."
            : $"Could not resolve '{masterName}', a master file declared by {pluginName}. This game separates " +
              "master files by type in the load order, so that master must be present in the Data directory being " +
              "processed before any selected plugin can be read.";
    }
}

/// <summary>
///     Base request for one Processing Run against a FormID Record Store.
/// </summary>
public abstract record ProcessingRunRequest
{
    private protected ProcessingRunRequest(
        string databasePath,
        GameRelease gameRelease,
        UpdateMode updateMode,
        bool dryRun)
    {
        if (!dryRun && string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ProcessingRunValidationException("Database path must be specified");
        }

        DatabasePath = databasePath;
        GameRelease = gameRelease;
        UpdateMode = updateMode;
        DryRun = dryRun;
    }

    /// <summary>
    ///     The SQLite database path used by this Processing Run.
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    ///     The GameRelease whose FormID table is read or written.
    /// </summary>
    public GameRelease GameRelease { get; }

    /// <summary>
    ///     Controls whether ingested Plugin records are appended or replace existing Plugin rows.
    /// </summary>
    public UpdateMode UpdateMode { get; }

    /// <summary>
    ///     When true, the run reports what would be processed without opening the FormID Record Store.
    /// </summary>
    public bool DryRun { get; }
}

/// <summary>
///     Request for a Processing Run that ingests selected Plugin files.
/// </summary>
public sealed record PluginProcessingRunRequest : ProcessingRunRequest
{
    /// <summary>
    ///     Creates a Plugin ingestion Processing Run request.
    /// </summary>
    /// <param name="gameDirectory">The selected game root or Data directory.</param>
    /// <param name="databasePath">The SQLite database path to write.</param>
    /// <param name="gameRelease">The GameRelease whose load-order and table rules apply.</param>
    /// <param name="pluginNames">The selected Plugin names to process, captured as an immutable snapshot.</param>
    /// <param name="updateMode">The storage update behavior for ingested Plugin records.</param>
    /// <param name="dryRun">Whether to report the planned work without writing to the store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pluginNames" /> is <see langword="null" />.</exception>
    /// <exception cref="ProcessingRunValidationException">
    ///     A required path or Plugin name is blank, the selection is empty, or Plugin names are duplicated.
    /// </exception>
    public PluginProcessingRunRequest(
        string? gameDirectory,
        string databasePath,
        GameRelease gameRelease,
        IEnumerable<string> pluginNames,
        UpdateMode updateMode,
        bool dryRun = false)
        : base(databasePath, gameRelease, updateMode, dryRun)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new ProcessingRunValidationException("Game directory must be specified when processing plugins");
        }

        ArgumentNullException.ThrowIfNull(pluginNames);

        try
        {
            PluginNames = PluginSelectionSnapshot.Capture(pluginNames);
        }
        catch (ArgumentException ex)
        {
            // Public requests translate internal selection invariants into the Processing Run validation contract.
            throw new ProcessingRunValidationException(ex.Message);
        }

        GameDirectory = gameDirectory;
    }

    /// <summary>
    ///     The selected game root or Data directory.
    /// </summary>
    public string GameDirectory { get; }

    /// <summary>
    ///     The selected Plugin names captured at run start.
    /// </summary>
    public IReadOnlyList<string> PluginNames { get; }
}

/// <summary>
///     Request for a Processing Run that imports a pipe-delimited FormID text file.
/// </summary>
public sealed record FormIdTextProcessingRunRequest : ProcessingRunRequest
{
    /// <summary>
    ///     Creates a FormID text-file Processing Run request.
    /// </summary>
    /// <param name="formIdListPath">The pipe-delimited FormID text file to import.</param>
    /// <param name="databasePath">The SQLite database path to write.</param>
    /// <param name="gameRelease">The GameRelease whose table receives the imported rows.</param>
    /// <param name="updateMode">The storage update behavior for Plugins found in the text file.</param>
    /// <param name="dryRun">Whether to report the planned work without writing to the store.</param>
    public FormIdTextProcessingRunRequest(
        string? formIdListPath,
        string databasePath,
        GameRelease gameRelease,
        UpdateMode updateMode,
        bool dryRun = false)
        : base(databasePath, gameRelease, updateMode, dryRun)
    {
        if (string.IsNullOrWhiteSpace(formIdListPath))
        {
            throw new ProcessingRunValidationException("FormID text file must be specified");
        }

        FormIdListPath = formIdListPath;
    }

    /// <summary>
    ///     The pipe-delimited FormID text file to import.
    /// </summary>
    public string FormIdListPath { get; }
}

/// <summary>
///     How one Processing Run ended, as a value the caller can inspect instead of prose it has to reconstruct from
///     status strings and exception types.
/// </summary>
/// <remarks>
///     <para>
///         The cases mirror the request union and carry the native reports the run already produced, so between them
///         they cover every terminal state a run can reach: a <see cref="PluginRunOutcome" />'s report distinguishes a
///         run that completed cleanly from one that completed with Processing Warnings and one that completed with
///         Failed Plugins, and <see cref="CancelledRunOutcome" /> is the fourth state.
///     </para>
///     <para>
///         Cancellation is an outcome rather than an exception because the token is executor-owned: the only
///         <see cref="OperationCanceledException" /> that can surface from a run is the one that executor requested,
///         so it can be matched precisely. Failure is deliberately not an outcome, because building one would need a
///         broad <c>catch (Exception)</c>, which ADR-0006 and the Plugin overlay reader avoid so that unexpected
///         internal failures abort loudly instead of being reported as a tidy result.
///     </para>
///     <para>
///         The base is public because the executor that returns it is; every case is internal because the reports the
///         cases carry are. Derivation is closed to this assembly, exactly as it is for the request union.
///     </para>
/// </remarks>
public abstract record ProcessingRunOutcome
{
    private protected ProcessingRunOutcome()
    {
    }
}

/// <summary>
///     A selected-Plugin Processing Run that reached its terminal state, carrying the authoritative ordered report.
/// </summary>
/// <param name="Report">The complete Plugin Ingestion report, in selected-Plugin order.</param>
internal sealed record PluginRunOutcome(PluginIngestionReport Report) : ProcessingRunOutcome;

/// <summary>
///     A FormID text-file Processing Run that reached its terminal state, carrying the Store's import counts.
/// </summary>
/// <param name="ImportResult">The distinct Plugin and valid record counts the Store confirmed.</param>
internal sealed record FormIdTextRunOutcome(FormIdTextFileImportResult ImportResult) : ProcessingRunOutcome;

/// <summary>
///     A dry run, which reports the work it would do without opening a FormID Record Store.
/// </summary>
/// <param name="Plan">The work the run would have performed.</param>
internal sealed record PlannedRunOutcome(ProcessingRunPlan Plan) : ProcessingRunOutcome;

/// <summary>
///     A Processing Run that stopped because this executor's own cancellation was requested.
/// </summary>
internal sealed record CancelledRunOutcome : ProcessingRunOutcome;

/// <summary>
///     The work a dry run would perform, mirroring the request union.
/// </summary>
/// <remarks>
///     The plan is currently shallow — it carries only what the request named. Resolving load order and reporting
///     would-ingest and would-skip per Plugin is the substantive dry run, which is deliberately a later change.
/// </remarks>
internal abstract record ProcessingRunPlan;

/// <summary>
///     The planned work of a selected-Plugin dry run.
/// </summary>
/// <param name="PluginNames">The selected Plugin names, in selection order.</param>
internal sealed record PluginRunPlan(ImmutableArray<string> PluginNames) : ProcessingRunPlan;

/// <summary>
///     The planned work of a FormID text-file dry run.
/// </summary>
/// <param name="FormIdListPath">The pipe-delimited FormID text file the run would import.</param>
internal sealed record FormIdTextRunPlan(string FormIdListPath) : ProcessingRunPlan;

/// <summary>
///     The kind of event emitted by a Processing Run.
/// </summary>
public enum ProcessingRunEventKind
{
    /// <summary>
    ///     A status/progress update that can be shown as the current run status.
    /// </summary>
    Status,

    /// <summary>
    ///     A non-fatal Processing Warning that should be shown separately from errors.
    /// </summary>
    Warning,

    /// <summary>
    ///     A failed Plugin or fatal run error message associated with the run.
    /// </summary>
    Error
}

/// <summary>
///     A typed event emitted by a Processing Run.
/// </summary>
/// <param name="Kind">The event kind.</param>
/// <param name="Message">The user-facing event message.</param>
/// <param name="Value">Optional progress percentage.</param>
public readonly record struct ProcessingRunEvent(ProcessingRunEventKind Kind, string Message, double? Value = null)
{
    /// <summary>
    ///     Creates a status/progress event.
    /// </summary>
    /// <param name="message">The user-facing status message.</param>
    /// <param name="value">Optional progress percentage.</param>
    public static ProcessingRunEvent Status(string message, double? value = null)
    {
        return new ProcessingRunEvent(ProcessingRunEventKind.Status, message, value);
    }

    /// <summary>
    ///     Creates a warning event.
    /// </summary>
    /// <param name="message">The user-facing warning message.</param>
    public static ProcessingRunEvent Warning(string message)
    {
        return new ProcessingRunEvent(ProcessingRunEventKind.Warning, message);
    }

    /// <summary>
    ///     Creates an error event.
    /// </summary>
    /// <param name="message">The user-facing error message.</param>
    public static ProcessingRunEvent Error(string message)
    {
        return new ProcessingRunEvent(ProcessingRunEventKind.Error, message);
    }
}

/// <summary>
///     Executes typed Processing Run requests and owns cancellation for the active run.
/// </summary>
internal interface IProcessingRunExecutor : IDisposable
{
    /// <summary>
    ///     Executes the supplied Processing Run request.
    /// </summary>
    /// <param name="request">The validated domain request describing the run.</param>
    /// <param name="progress">Optional typed run event reporter for transient status.</param>
    /// <returns>How the run ended, including cancellation this executor was asked for.</returns>
    /// <remarks>
    ///     Validation failures, <see cref="UnresolvableMasterException" /> and unexpected internal failures propagate:
    ///     only cancellation is a value, because only cancellation can be attributed to this executor precisely.
    /// </remarks>
    Task<ProcessingRunOutcome> ExecuteAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunEvent>? progress = null);

    /// <summary>
    ///     Requests cancellation for the active Processing Run, if one is active.
    /// </summary>
    void Cancel();
}
