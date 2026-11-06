using System.Diagnostics;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Processing Run Admission: decides, before a Processing Run exists, whether a press of Process FormIDs starts a
///     run and which run it starts (ADR-0010).
/// </summary>
internal static class ProcessingRunAdmission
{
    private const string AppDataDirectoryName = "FormID Database Manager";
    private const string DatabaseDirectoryName = "Databases";

    /// <summary>
    ///     Admits the captured inputs as one Processing Run request, or refuses them with the reason the user can act on.
    /// </summary>
    /// <param name="inputs">Everything captured when the user pressed the button.</param>
    /// <param name="defaultDatabaseRoot">
    ///     The root under which a real run with no chosen database path gets its default database, or null or blank when
    ///     none could be resolved.
    /// </param>
    /// <returns>An admitted run carrying its request, or a refused run carrying its reason.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     A run needs a default database path and the captured GameRelease is not a Supported GameRelease.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         The checks run in a fixed order: no GameRelease; then a non-blank FormID text path chooses a FormID text
    ///         run, which consults no Plugin List confirmation and to which only the database-location check still
    ///         applies; then a selected-Plugin run's Plugin List confirmation (none, loading, unavailable) and its
    ///         selection; and last the database location.
    ///     </para>
    ///     <para>
    ///         Whether the Plugin List holds a Confirmed Plugin List for the Game Context's source is the Plugin List's
    ///         answer, captured as the confirmation (ADR-0010 2026-11-05 amendment). Admission only maps a loading or
    ///         unavailable answer onto its own refusal reasons.
    ///     </para>
    ///     <para>
    ///         There is no exception mode for a directory. The Game Context builds the Plugin List Source when it adopts
    ///         a directory and refuses one that cannot become a source, so the source a confirmation was answered for is
    ///         always usable and a missing one is a refusal (ADR-0011).
    ///     </para>
    ///     <para>
    ///         This is pure: it reads no disk, creates nothing, and writes no ViewModel state. A defaulted database path
    ///         is only computed; the FormID Record Store creates its folder when a real run opens it (ADR-0001).
    ///     </para>
    /// </remarks>
    internal static RunAdmissionDecision Admit(ProcessingRunInputs inputs, string? defaultDatabaseRoot)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.GameRelease is not { } gameRelease)
        {
            return new RefusedRun(RunRejectionReason.NoGameSelected);
        }

        var formIdListPath = inputs.FormIdListPath;
        ConfirmedPluginList? confirmed = null;
        if (string.IsNullOrWhiteSpace(formIdListPath))
        {
            // The Game Context holds a directory only when it makes a source, and the confirmation is null exactly when
            // it holds no source, so no confirmation is what "no game directory" means to the user (ADR-0011). The
            // Plugin List's loading and unavailable answers map onto admission's own reasons here, so a refusal reason
            // never crosses into the Plugin List.
            switch (inputs.PluginListConfirmation)
            {
                case null:
                    return new RefusedRun(RunRejectionReason.NoGameDirectory);
                case ConfirmedPluginListLoading:
                    return new RefusedRun(RunRejectionReason.PluginListLoading);
                case ConfirmedPluginListUnavailable:
                    return new RefusedRun(RunRejectionReason.PluginListUnavailable);
                case ConfirmedPluginListAvailable available:
                    confirmed = available.Confirmed;
                    break;
                default:
                    // The confirmation union is closed to Core, so no other case can be constructed.
                    throw new UnreachableException();
            }

            if (confirmed.SelectedPluginNames.IsEmpty)
            {
                return new RefusedRun(RunRejectionReason.NoPluginsSelected);
            }
        }

        string? defaultedDatabasePath = null;
        var databasePath = inputs.DatabasePath ?? string.Empty;
        // Blank rather than merely empty counts as no path, because a whitespace path names no file and would
        // otherwise reach the request constructor's programming-error guard. A Dry Run opens no Store, so it neither
        // needs a path nor gets one chosen on the user's behalf: defaulting here would leave a path they never picked
        // in the box after a run that deliberately wrote nothing.
        if (string.IsNullOrWhiteSpace(databasePath) && !inputs.DryRun)
        {
            if (string.IsNullOrWhiteSpace(defaultDatabaseRoot))
            {
                return new RefusedRun(RunRejectionReason.NoDatabaseLocation);
            }

            defaultedDatabasePath = CreateDefaultDatabasePath(gameRelease, defaultDatabaseRoot);
            databasePath = defaultedDatabasePath;
        }

        // Only a FormID text run reaches this point without a matched confirmation. The request carries the source the
        // list was confirmed against rather than the Game Context's: the two are equal, but the confirmed one keeps the
        // exact Data-directory spelling the Plugin List was discovered from, and that is where Plugins are read.
        ProcessingRunRequest request = confirmed is null
            ? new FormIdTextProcessingRunRequest(
                formIdListPath,
                databasePath,
                gameRelease,
                inputs.UpdateMode,
                inputs.DryRun)
            : new PluginProcessingRunRequest(
                confirmed.Source,
                databasePath,
                confirmed.SelectedPluginNames,
                inputs.UpdateMode,
                inputs.DryRun);
        return new AdmittedRun(request, defaultedDatabasePath);
    }

    /// <summary>
    ///     Computes the default database path for a GameRelease under the default database root.
    /// </summary>
    /// <param name="gameRelease">The selected GameRelease, whose Store table name is the file name.</param>
    /// <param name="defaultDatabaseRoot">The non-blank root resolved by production composition.</param>
    /// <returns>The full path of the default database file. Nothing is created on disk.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gameRelease" /> is not a Supported GameRelease.</exception>
    private static string CreateDefaultDatabasePath(GameRelease gameRelease, string defaultDatabaseRoot)
    {
        var databaseDirectory = Path.Combine(defaultDatabaseRoot, AppDataDirectoryName, DatabaseDirectoryName);

        // The Store's own table name is the filename, so a database file and the table inside it cannot disagree.
        return Path.Combine(databaseDirectory, $"{SupportedGameReleases.ForRelease(gameRelease).TableName}.db");
    }
}

/// <summary>
///     The inputs captured when the user pressed Process FormIDs, fixed before any startup notification can edit them.
/// </summary>
/// <remarks>
///     The properties are get-only, so a <c>with</c> expression cannot pair a GameRelease and a confirmation that the
///     constructor's guard never saw.
/// </remarks>
internal sealed record ProcessingRunInputs
{
    /// <summary>
    ///     Captures the inputs of one press, checking that the GameRelease and the Plugin List confirmation could have come
    ///     from one read of the Game Context.
    /// </summary>
    /// <param name="gameRelease">The selected GameRelease, or null when none is selected.</param>
    /// <param name="pluginListConfirmation">
    ///     The Plugin List's confirmation for the Game Context's Plugin List Source, answered from one Plugin List
    ///     snapshot, or null exactly when the Game Context holds no Plugin List Source (ADR-0010 2026-11-05 amendment).
    /// </param>
    /// <param name="formIdListPath">The optional FormID text path; a non-blank one chooses a FormID text run.</param>
    /// <param name="databasePath">The chosen database path, before any default is applied.</param>
    /// <param name="updateMode">The Store update behavior for the run.</param>
    /// <param name="dryRun">Whether the run only produces a Processing Run Plan.</param>
    /// <exception cref="ArgumentException">
    ///     A confirmation is present without a GameRelease, or a matching Confirmed Plugin List belongs to a GameRelease
    ///     other than <paramref name="gameRelease" />. Either is a programming error in the capture, not a refusal.
    /// </exception>
    internal ProcessingRunInputs(
        GameRelease? gameRelease,
        PluginListConfirmation? pluginListConfirmation,
        string? formIdListPath,
        string? databasePath,
        UpdateMode updateMode,
        bool dryRun)
    {
        // Both come from one read of the Game Context, which holds a source only beside a GameRelease and builds the
        // source from that release (ADR-0011). A mismatch can only be a broken capture, so it fails here rather than
        // reaching admission as a misleading refusal or a run against the wrong game.
        if (pluginListConfirmation is not null && gameRelease is null)
        {
            throw new ArgumentException(
                "A Plugin List confirmation was captured without a GameRelease.",
                nameof(pluginListConfirmation));
        }

        if (pluginListConfirmation is ConfirmedPluginListAvailable available &&
            available.Confirmed.Source.GameRelease != gameRelease)
        {
            throw new ArgumentException(
                "The captured Confirmed Plugin List belongs to a GameRelease other than the captured one.",
                nameof(pluginListConfirmation));
        }

        GameRelease = gameRelease;
        PluginListConfirmation = pluginListConfirmation;
        FormIdListPath = formIdListPath;
        DatabasePath = databasePath;
        UpdateMode = updateMode;
        DryRun = dryRun;
    }

    /// <summary>Gets the selected GameRelease, or null when none is selected.</summary>
    public GameRelease? GameRelease { get; }

    /// <summary>
    ///     Gets the Plugin List's confirmation for the Game Context's Plugin List Source, or null when the Game Context
    ///     holds no source. A matching Confirmed Plugin List's GameRelease is <see cref="GameRelease" />.
    /// </summary>
    public PluginListConfirmation? PluginListConfirmation { get; }

    /// <summary>Gets the optional FormID text path; a non-blank one chooses a FormID text run.</summary>
    public string? FormIdListPath { get; }

    /// <summary>Gets the chosen database path, before any default is applied.</summary>
    public string? DatabasePath { get; }

    /// <summary>Gets the Store update behavior for the run.</summary>
    public UpdateMode UpdateMode { get; }

    /// <summary>Gets whether the run only produces a Processing Run Plan.</summary>
    public bool DryRun { get; }
}

/// <summary>
///     Why Processing Run Admission refused a press. The words for each reason live only in
///     <see cref="ProcessingRunPresentation" />.
/// </summary>
internal enum RunRejectionReason
{
    /// <summary>No GameRelease is selected.</summary>
    NoGameSelected,

    /// <summary>A selected-Plugin run has no game directory, so the Game Context holds no Plugin List Source.</summary>
    NoGameDirectory,

    /// <summary>
    ///     The captured Plugin List confirmation says the list for the Game Context's Plugin List Source is still loading.
    /// </summary>
    PluginListLoading,

    /// <summary>
    ///     The captured Plugin List confirmation says the list for the Game Context's Plugin List Source did not load.
    /// </summary>
    PluginListUnavailable,

    /// <summary>The matching Confirmed Plugin List selects no Plugins.</summary>
    NoPluginsSelected,

    /// <summary>A real run has no database path and no usable default database root.</summary>
    NoDatabaseLocation
}

/// <summary>
///     What Processing Run Admission decided about one press: an admitted run or a refused one.
/// </summary>
/// <remarks>
///     Derivation is closed to this assembly, as it is for the request and outcome unions, so the two cases below are
///     the only ones a caller has to handle.
/// </remarks>
internal abstract record RunAdmissionDecision
{
    private protected RunAdmissionDecision()
    {
    }
}

/// <summary>
///     A press admitted as one Processing Run.
/// </summary>
/// <param name="Request">The request the run executes.</param>
/// <param name="DefaultedDatabasePath">
///     The database path admission supplied because none was chosen, or null when the request uses the captured path.
/// </param>
internal sealed record AdmittedRun(ProcessingRunRequest Request, string? DefaultedDatabasePath) : RunAdmissionDecision;

/// <summary>
///     A press refused before any Processing Run existed.
/// </summary>
/// <param name="Reason">Why the press was refused.</param>
internal sealed record RefusedRun(RunRejectionReason Reason) : RunAdmissionDecision;
