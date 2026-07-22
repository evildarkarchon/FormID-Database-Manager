using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Defines the complete selected-Plugin phase used by a Processing Run.
/// </summary>
internal interface IPluginIngestion
{
    /// <summary>
    ///     Ingests the captured selection through the already-open run-scoped FormID Record Store session.
    /// </summary>
    /// <param name="request">The immutable selected-Plugin request.</param>
    /// <param name="recordStore">The Store session owned by the surrounding Processing Run.</param>
    /// <param name="progress">Optional transient preparation and current-Plugin facts.</param>
    /// <param name="cancellationToken">Stops the selected set without returning a completed report.</param>
    /// <returns>The authoritative ordered outcome report for the complete selection.</returns>
    /// <remarks>
    ///     Plugin-specific read failures may become typed outcomes. Cancellation and infrastructure failures propagate
    ///     without a report; Store optimization and disposal remain the surrounding Processing Run's responsibility.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> requests cancellation.</exception>
    [RequiresUnreferencedCode(
        "Uses reflection-based name extraction for Mutagen records through EntryExtraction.")]
    Task<PluginIngestionReport> IngestAsync(
        SelectedPluginIngestionRequest request,
        IFormIdRecordStoreSession recordStore,
        IProgress<PluginIngestionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Identifies the transient phase represented by Plugin Ingestion progress facts.
/// </summary>
internal enum PluginIngestionProgressStage
{
    /// <summary>
    ///     Load-order preparation is active and no selected Plugin has started.
    /// </summary>
    PreparingLoadOrder,

    /// <summary>
    ///     One selected Plugin is the current attempt.
    /// </summary>
    IngestingPlugin
}

/// <summary>
///     Structured transient progress for load-order preparation or the current selected Plugin.
/// </summary>
internal sealed record PluginIngestionProgress
{
    private PluginIngestionProgress(
        PluginIngestionProgressStage stage,
        string? pluginName,
        int? pluginPosition,
        int totalPluginCount)
    {
        Stage = stage;
        PluginName = pluginName;
        PluginPosition = pluginPosition;
        TotalPluginCount = totalPluginCount;
    }

    /// <summary>
    ///     Creates load-order preparation progress for the captured selection.
    /// </summary>
    /// <param name="totalPluginCount">The positive selected Plugin count.</param>
    /// <returns>Preparation progress without a current Plugin.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="totalPluginCount" /> is not positive.</exception>
    public static PluginIngestionProgress PreparingLoadOrder(int totalPluginCount)
    {
        EnsurePositiveTotal(totalPluginCount);
        return new PluginIngestionProgress(
            PluginIngestionProgressStage.PreparingLoadOrder,
            null,
            null,
            totalPluginCount);
    }

    /// <summary>
    ///     Creates current-Plugin progress at a one-based selection position.
    /// </summary>
    /// <param name="pluginName">The current selected Plugin.</param>
    /// <param name="pluginPosition">The one-based position in the captured selection.</param>
    /// <param name="totalPluginCount">The positive selected Plugin count.</param>
    /// <returns>Progress facts for the current Plugin attempt.</returns>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The total is not positive, or the position falls outside the selected Plugin count.
    /// </exception>
    public static PluginIngestionProgress IngestingPlugin(
        string pluginName,
        int pluginPosition,
        int totalPluginCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        EnsurePositiveTotal(totalPluginCount);
        if (pluginPosition <= 0 || pluginPosition > totalPluginCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pluginPosition),
                "Plugin position must fall within the selected Plugin count.");
        }

        return new PluginIngestionProgress(
            PluginIngestionProgressStage.IngestingPlugin,
            pluginName,
            pluginPosition,
            totalPluginCount);
    }

    /// <summary>
    ///     The active Plugin Ingestion stage.
    /// </summary>
    public PluginIngestionProgressStage Stage { get; }

    /// <summary>
    ///     The current selected Plugin, or <see langword="null" /> during preparation.
    /// </summary>
    public string? PluginName { get; }

    /// <summary>
    ///     The one-based selection position, or <see langword="null" /> during preparation.
    /// </summary>
    public int? PluginPosition { get; }

    /// <summary>
    ///     The total selected Plugin count.
    /// </summary>
    public int TotalPluginCount { get; }

    private static void EnsurePositiveTotal(int totalPluginCount)
    {
        if (totalPluginCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalPluginCount),
                "Plugin Ingestion progress requires at least one selected Plugin.");
        }
    }
}

/// <summary>
///     Captures the complete selected-Plugin input for one Plugin Ingestion operation.
/// </summary>
internal sealed record SelectedPluginIngestionRequest
{
    /// <summary>
    ///     Creates an immutable snapshot of the selected Plugins and their execution order.
    /// </summary>
    /// <param name="gameDirectory">The selected game root or Data directory.</param>
    /// <param name="gameRelease">The GameRelease whose Plugin rules apply.</param>
    /// <param name="pluginNames">The selected Plugin names in execution order.</param>
    /// <param name="updateMode">The Store update behavior for Ingested Plugins.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pluginNames" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     The game directory or a Plugin name is blank, the selection is empty, or Plugin names are duplicated.
    /// </exception>
    public SelectedPluginIngestionRequest(
        string gameDirectory,
        GameRelease gameRelease,
        IEnumerable<string> pluginNames,
        UpdateMode updateMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentNullException.ThrowIfNull(pluginNames);

        GameDirectory = gameDirectory;
        GameRelease = gameRelease;
        PluginNames = PluginSelectionSnapshot.Capture(pluginNames);
        UpdateMode = updateMode;
    }

    /// <summary>
    ///     The selected game root or Data directory.
    /// </summary>
    public string GameDirectory { get; }

    /// <summary>
    ///     The GameRelease whose Plugin rules apply.
    /// </summary>
    public GameRelease GameRelease { get; }

    /// <summary>
    ///     The immutable selected Plugin names in execution order.
    /// </summary>
    public ImmutableArray<string> PluginNames { get; }

    /// <summary>
    ///     The Store update behavior for Ingested Plugins.
    /// </summary>
    public UpdateMode UpdateMode { get; }
}

/// <summary>
///     Authoritative ordered outcomes for one complete selected-Plugin operation.
/// </summary>
internal sealed record PluginIngestionReport
{
    /// <summary>
    ///     Creates a report whose outcome cardinality and order agree with the captured request.
    /// </summary>
    /// <param name="request">The immutable selected-Plugin request.</param>
    /// <param name="outcomes">One outcome for every selected Plugin in request order.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     Outcomes contain a null entry or disagree with the request cardinality, names, or order.
    /// </exception>
    public PluginIngestionReport(
        SelectedPluginIngestionRequest request,
        IEnumerable<PluginIngestionOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(outcomes);

        var outcomeSnapshot = ImmutableArray.CreateRange(outcomes);
        if (outcomeSnapshot.Any(static outcome => outcome is null))
        {
            throw new ArgumentException("Plugin Ingestion outcomes must not contain null entries.", nameof(outcomes));
        }

        if (outcomeSnapshot.Length != request.PluginNames.Length)
        {
            throw new ArgumentException(
                "Plugin Ingestion must report exactly one outcome for every selected Plugin.",
                nameof(outcomes));
        }

        for (var index = 0; index < outcomeSnapshot.Length; index++)
        {
            // Validate rather than reorder because each outcome position maps back to the authoritative selection.
            if (!string.Equals(
                    request.PluginNames[index],
                    outcomeSnapshot[index].PluginName,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Plugin Ingestion outcomes must preserve selected Plugin names and order.",
                    nameof(outcomes));
            }
        }

        Outcomes = outcomeSnapshot;
    }

    /// <summary>
    ///     The immutable typed outcomes in selected-Plugin order.
    /// </summary>
    public ImmutableArray<PluginIngestionOutcome> Outcomes { get; }
}

/// <summary>
///     A typed outcome for one selected Plugin at its authoritative report position.
/// </summary>
internal abstract record PluginIngestionOutcome
{
    /// <summary>
    ///     Creates an outcome for the selected Plugin name retained by the request.
    /// </summary>
    /// <param name="pluginName">The selected Plugin name.</param>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is blank.</exception>
    protected PluginIngestionOutcome(string pluginName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        PluginName = pluginName;
    }

    /// <summary>
    ///     The selected Plugin name represented by this outcome.
    /// </summary>
    public string PluginName { get; }
}

/// <summary>
///     A selected Plugin for which one or more FormID records were stored.
/// </summary>
internal sealed record IngestedPlugin : PluginIngestionOutcome
{
    /// <summary>
    ///     Creates an Ingested Plugin with its Store-confirmed count and optional warning facts.
    /// </summary>
    /// <param name="pluginName">The selected Plugin name.</param>
    /// <param name="formIdCount">The positive number of stored FormID records.</param>
    /// <param name="warning">Optional recoverable Processing Warning facts.</param>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="formIdCount" /> is not positive.</exception>
    public IngestedPlugin(string pluginName, int formIdCount, ProcessingWarning? warning = null)
        : base(pluginName)
    {
        if (formIdCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(formIdCount), "An Ingested Plugin must store at least one FormID record.");
        }

        FormIdCount = formIdCount;
        Warning = warning;
    }

    /// <summary>
    ///     The number of FormID records stored for the Plugin.
    /// </summary>
    public int FormIdCount { get; }

    /// <summary>
    ///     Recoverable Processing Warning facts, when record issues occurred.
    /// </summary>
    public ProcessingWarning? Warning { get; }
}

/// <summary>
///     The stable reasons a selected Plugin can be skipped without failing Plugin Ingestion.
/// </summary>
internal enum SkippedPluginReason
{
    /// <summary>
    ///     The selected Plugin was absent from the prepared load-order snapshot.
    /// </summary>
    NotPresentInLoadOrder,

    /// <summary>
    ///     The selected Plugin file was unavailable at ingestion time.
    /// </summary>
    PluginFileUnavailable,

    /// <summary>
    ///     The selected Plugin produced no FormID records.
    /// </summary>
    ZeroFormIdRecords
}

/// <summary>
///     A selected Plugin that stored no records for one typed nonfatal reason.
/// </summary>
internal sealed record SkippedPlugin : PluginIngestionOutcome
{
    /// <summary>
    ///     Creates a Skipped Plugin fact.
    /// </summary>
    /// <param name="pluginName">The selected Plugin name.</param>
    /// <param name="reason">The stable reason no records were stored.</param>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason" /> is not defined.</exception>
    public SkippedPlugin(string pluginName, SkippedPluginReason reason)
        : base(pluginName)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        Reason = reason;
    }

    /// <summary>
    ///     The stable reason no records were stored.
    /// </summary>
    public SkippedPluginReason Reason { get; }
}

/// <summary>
///     The stable reason a Plugin-specific failure becomes a Failed Plugin.
/// </summary>
internal enum FailedPluginReason
{
    /// <summary>
    ///     A Plugin-specific read failed without failing the surrounding Processing Run.
    /// </summary>
    PluginReadFailed
}

/// <summary>
///     The internal Plugin-read phase retained for diagnostic reporting.
/// </summary>
internal enum PluginReadPhase
{
    /// <summary>
    ///     Plugin opening failed before records could be read.
    /// </summary>
    OpeningPlugin,

    /// <summary>
    ///     Record enumeration or extraction failed after the Plugin opened.
    /// </summary>
    ReadingRecords
}

/// <summary>
///     Diagnostic facts retained for a Plugin-specific read failure.
/// </summary>
internal sealed record PluginReadDiagnostic
{
    /// <summary>
    ///     Creates diagnostic facts without promoting internal phases into stable failure reasons.
    /// </summary>
    /// <param name="phase">The internal read phase that failed.</param>
    /// <param name="message">The underlying failure message.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase" /> is not defined.</exception>
    /// <exception cref="ArgumentException"><paramref name="message" /> is blank.</exception>
    public PluginReadDiagnostic(PluginReadPhase phase, string message)
    {
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Phase = phase;
        Message = message;
    }

    /// <summary>
    ///     The internal Plugin-read phase that failed.
    /// </summary>
    public PluginReadPhase Phase { get; }

    /// <summary>
    ///     The underlying failure message retained as diagnostics rather than presentation wording.
    /// </summary>
    public string Message { get; }
}

/// <summary>
///     A selected Plugin whose Plugin-specific read could not complete.
/// </summary>
internal sealed record FailedPlugin : PluginIngestionOutcome
{
    /// <summary>
    ///     Creates a Failed Plugin with the stable read-failed reason and internal diagnostics.
    /// </summary>
    /// <param name="pluginName">The selected Plugin name.</param>
    /// <param name="diagnostic">The internal phase and underlying failure message.</param>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostic" /> is <see langword="null" />.</exception>
    public FailedPlugin(string pluginName, PluginReadDiagnostic diagnostic)
        : base(pluginName)
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    /// <summary>
    ///     The stable classification exposed to Processing Run.
    /// </summary>
    public FailedPluginReason Reason => FailedPluginReason.PluginReadFailed;

    /// <summary>
    ///     Internal Plugin-read diagnostics kept separate from the stable reason.
    /// </summary>
    public PluginReadDiagnostic Diagnostic { get; }
}

/// <summary>
///     Bounded diagnostic facts for recoverable record issues in an Ingested Plugin.
/// </summary>
internal sealed record ProcessingWarning
{
    /// <summary>
    ///     The maximum ordered diagnostic details retained for one Ingested Plugin.
    /// </summary>
    public const int MaximumDiagnosticDetailCount = 5;

    /// <summary>
    ///     Creates warning facts while bounding retained diagnostic detail.
    /// </summary>
    /// <param name="totalIssueCount">The total recoverable issue count.</param>
    /// <param name="diagnosticDetails">Available diagnostic details in observation order.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="totalIssueCount" /> is not positive.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="diagnosticDetails" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     A detail is blank, or more details are supplied than the total issue count.
    /// </exception>
    public ProcessingWarning(int totalIssueCount, IEnumerable<string> diagnosticDetails)
    {
        if (totalIssueCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalIssueCount), "A Processing Warning must represent at least one issue.");
        }

        ArgumentNullException.ThrowIfNull(diagnosticDetails);
        var detailSnapshot = ImmutableArray.CreateRange(diagnosticDetails);
        if (detailSnapshot.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Diagnostic details must not be blank.", nameof(diagnosticDetails));
        }

        if (detailSnapshot.Length > totalIssueCount)
        {
            throw new ArgumentException("Diagnostic detail count cannot exceed the total issue count.", nameof(diagnosticDetails));
        }

        TotalIssueCount = totalIssueCount;
        // Retain the authoritative total while bounding untrusted per-record diagnostic growth.
        DiagnosticDetails = detailSnapshot.Take(MaximumDiagnosticDetailCount).ToImmutableArray();
    }

    /// <summary>
    ///     The total number of recoverable record issues.
    /// </summary>
    public int TotalIssueCount { get; }

    /// <summary>
    ///     Up to the first five diagnostic details in observation order.
    /// </summary>
    public ImmutableArray<string> DiagnosticDetails { get; }

    /// <summary>
    ///     The number of issues not represented by retained diagnostic details.
    /// </summary>
    public int OmittedDetailCount => TotalIssueCount - DiagnosticDetails.Length;
}

/// <summary>
///     Captures and validates the Plugin selection shared by public Processing Run and internal ingestion requests.
/// </summary>
internal static class PluginSelectionSnapshot
{
    /// <summary>
    ///     Copies Plugin names while enforcing the selection invariants required before Store opening.
    /// </summary>
    /// <param name="pluginNames">The selected Plugin names in execution order.</param>
    /// <returns>An immutable selection snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pluginNames" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The selection is empty, contains a blank name, or contains a duplicate.</exception>
    internal static ImmutableArray<string> Capture(IEnumerable<string> pluginNames)
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
