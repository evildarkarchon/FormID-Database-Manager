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
            PluginNames = CapturePluginNames(pluginNames);
        }
        catch (ArgumentException ex)
        {
            // Keep selection and enumeration argument failures on the Processing Run validation contract.
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

    /// <summary>
    ///     Copies Plugin names while enforcing the selection invariants required before Store opening.
    /// </summary>
    /// <param name="pluginNames">The selected Plugin names in execution order.</param>
    /// <returns>An immutable selection snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pluginNames" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The selection is empty, contains a blank name, or contains a duplicate.</exception>
    private static ImmutableArray<string> CapturePluginNames(IEnumerable<string> pluginNames)
    {
        ArgumentNullException.ThrowIfNull(pluginNames);

        var snapshot = ImmutableArray.CreateRange(pluginNames);
        if (snapshot.IsEmpty)
        {
            throw new ArgumentException("No plugins selected");
        }

        if (snapshot.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Plugin name must be specified");
        }

        // Plugin selection identity is case-insensitive so casing variants cannot write the same Plugin twice.
        if (snapshot.Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
        {
            throw new ArgumentException("Plugin names must be unique");
        }

        return snapshot;
    }
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
internal abstract record ProcessingRunPlan;

/// <summary>
///     The planned work of a selected-Plugin dry run.
/// </summary>
/// <param name="Plan">What Plugin Ingestion would do to each selected Plugin, in selection order.</param>
/// <remarks>
///     The plan is substantive: Plugin Ingestion resolved the Data path, prepared the load order and opened every
///     selected Plugin's overlay to produce it, so this says what a run would do rather than echoing the names the
///     user selected.
/// </remarks>
internal sealed record PluginRunPlan(PluginIngestionPlan Plan) : ProcessingRunPlan;

/// <summary>
///     The planned work of a FormID text-file dry run, which is a file-metadata lookup and nothing more.
/// </summary>
/// <param name="FormIdListPath">The pipe-delimited FormID text file the run would import.</param>
/// <param name="SizeInBytes">
///     The file's size in bytes, or <see langword="null" /> when no file exists at that path.
/// </param>
/// <remarks>
///     A text plan deliberately does not open, parse or count the file's rows. Row counts are exactly what an import
///     measures, and measuring them would be doing the run rather than planning it — so presence and size are the two
///     honest facts a plan can report about the file.
/// </remarks>
internal sealed record FormIdTextRunPlan(string FormIdListPath, long? SizeInBytes) : ProcessingRunPlan;

/// <summary>
///     What a Processing Run is doing right now, as facts the run itself defines rather than words it writes.
/// </summary>
/// <remarks>
///     <para>
///         This is the run's own vocabulary, not a collaborator's forwarded on: Plugin Ingestion's stage enum and the
///         FormID Record Store's counters are both translated into these cases, so the run says what it is doing in
///         terms of its own work rather than in terms of whichever collaborator happens to be doing it.
///     </para>
///     <para>
///         Every case is transient. How a run <em>ended</em> is a <see cref="ProcessingRunOutcome" />, because the
///         progress channel is handed back as soon as a run finishes and anything terminal written here is erased
///         before the user can read it (issue #60).
///     </para>
///     <para>
///         The base is public because the executor that reports it is; every case is internal because only this
///         assembly renders them. Derivation is closed to this assembly, exactly as it is for the request and outcome
///         unions.
///     </para>
/// </remarks>
public abstract record ProcessingRunProgress
{
    private protected ProcessingRunProgress()
    {
    }
}

/// <summary>
///     Load order is being prepared for the captured selection, before any Plugin has been read.
/// </summary>
/// <param name="TotalPluginCount">The number of selected Plugins the run will attempt.</param>
internal sealed record PreparingLoadOrder(int TotalPluginCount) : ProcessingRunProgress;

/// <summary>
///     One selected Plugin is being ingested at a position within the captured selection.
/// </summary>
/// <param name="PluginName">The Plugin currently being read.</param>
/// <param name="Position">The one-based position of that Plugin in the captured selection.</param>
/// <param name="TotalPluginCount">The number of selected Plugins the run will attempt.</param>
internal sealed record IngestingPlugin(string PluginName, int Position, int TotalPluginCount) : ProcessingRunProgress;

/// <summary>
///     A FormID text file is being imported, reported as the counters the Store measured.
/// </summary>
/// <param name="RecordCount">The number of valid FormID text rows counted so far.</param>
/// <param name="BytesRead">How far into the file the reader has pulled, in bytes.</param>
/// <param name="TotalBytes">The total size of the FormID text file, in bytes.</param>
/// <param name="MostRecentPlugin">
///     The Plugin this report is about, or <see langword="null" /> when the report is about the file's progress
///     instead. Naming a Plugin is the run's decision and not the Store's: the Store reports every Plugin the first
///     time it sees one, and the run populates this only for the reports it wants shown by name.
/// </param>
/// <remarks>
///     The three things an import has to say are told apart by the counters rather than by three cases, and this is
///     the contract that says how: a report naming a Plugin is about that Plugin, a report naming none with no
///     records counted is the import opening, and anything else is the file's progress. Whoever produces one of
///     these owes the same reading — a report of no records that names no new Plugin means the import has started,
///     and must not be sent for anything else.
/// </remarks>
internal sealed record ImportingFormIdText(
    long RecordCount,
    long BytesRead,
    long TotalBytes,
    string? MostRecentPlugin) : ProcessingRunProgress;

/// <summary>
///     A FormID text import has finished, carrying the counts the Store confirmed.
/// </summary>
/// <param name="ImportResult">The distinct Plugin and valid record counts of the finished import.</param>
/// <remarks>
///     This is still transient progress rather than an outcome: it describes the import, which finishes before the
///     run does, and the run continues into Store maintenance afterwards. The counts are not derivable from
///     <see cref="ImportingFormIdText" /> — that case knows bytes and rows but never how many distinct Plugins the
///     file named — which is why the finished import is a case of its own.
/// </remarks>
internal sealed record ImportedFormIdText(FormIdTextFileImportResult ImportResult) : ProcessingRunProgress;

/// <summary>
///     The run is failing with the message of the exception about to be rethrown.
/// </summary>
/// <param name="FailureMessage">The failure's own message, without any prefix.</param>
/// <remarks>
///     A failure produces no outcome to render — only cancellation becomes a value — so the last thing a failing run
///     has to say goes out on the progress channel like everything else it says while running.
/// </remarks>
internal sealed record ProcessingRunFailure(string FailureMessage) : ProcessingRunProgress;

/// <summary>
///     Executes typed Processing Run requests and owns cancellation for the active run.
/// </summary>
internal interface IProcessingRunExecutor : IDisposable
{
    /// <summary>
    ///     Executes the supplied Processing Run request.
    /// </summary>
    /// <param name="request">The validated domain request describing the run.</param>
    /// <param name="progress">Optional typed reporter for the run's transient progress.</param>
    /// <returns>How the run ended, including cancellation this executor was asked for.</returns>
    /// <remarks>
    ///     Validation failures, <see cref="UnresolvableMasterException" /> and unexpected internal failures propagate:
    ///     only cancellation is a value, because only cancellation can be attributed to this executor precisely.
    /// </remarks>
    Task<ProcessingRunOutcome> ExecuteAsync(
        ProcessingRunRequest request,
        IProgress<ProcessingRunProgress>? progress = null);

    /// <summary>
    ///     Requests cancellation for the active Processing Run, if one is active.
    /// </summary>
    void Cancel();
}
