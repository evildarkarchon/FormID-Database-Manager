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
    ///         run, to which only the database-location check still applies; then a selected-Plugin run's Plugin List
    ///         Source, its Plugin List match and its selection; and last the database location.
    ///     </para>
    ///     <para>
    ///         There is no exception mode for a directory. The Game Context builds the Plugin List Source when it adopts
    ///         a directory and refuses one that cannot become a source, so a captured source is always usable and a
    ///         missing one is a refusal (ADR-0011).
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
            // The Game Context holds a directory only when it makes a source, so no source is what "no game directory"
            // means to the user (ADR-0011).
            if (inputs.PluginListSource is not { } capturedSource)
            {
                return new RefusedRun(RunRejectionReason.NoGameDirectory);
            }

            var state = inputs.PluginListState;
            confirmed = state.Confirmed;
            if (confirmed is null || confirmed.Source != capturedSource)
            {
                return new RefusedRun(MissingConfirmationReason(state, capturedSource));
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
        // list was confirmed against rather than the captured one: the two are equal, but the confirmed one keeps the
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

    /// <summary>
    ///     Explains why a Plugin List state holds no Confirmed Plugin List for the captured source.
    /// </summary>
    /// <param name="state">The captured Plugin List state, which has no matching confirmation.</param>
    /// <param name="capturedSource">The Plugin List Source captured from the Game Context.</param>
    /// <returns>
    ///     Loading while discovery is in flight or the list still describes another source, because a list for the
    ///     captured source is on its way; otherwise unavailable, because the captured source's discovery ended or never
    ///     started and only a retry brings one.
    /// </returns>
    private static RunRejectionReason MissingConfirmationReason(
        PluginListState state,
        PluginListSource capturedSource)
    {
        return state switch
        {
            PluginListRefreshingState => RunRejectionReason.PluginListLoading,
            SourcedPluginListState sourced when sourced.Source != capturedSource =>
                RunRejectionReason.PluginListLoading,
            _ => RunRejectionReason.PluginListUnavailable
        };
    }
}

/// <summary>
///     The inputs captured when the user pressed Process FormIDs, fixed before any startup notification can edit them.
/// </summary>
/// <param name="GameRelease">The selected GameRelease, or null when none is selected.</param>
/// <param name="PluginListSource">
///     The Game Context's Plugin List Source, or null when it holds no game directory. It is built from the same Game
///     Context state as <paramref name="GameRelease" />, so its GameRelease is the selected one.
/// </param>
/// <param name="PluginListState">The Plugin List's whole immutable state snapshot at the press.</param>
/// <param name="FormIdListPath">The optional FormID text path; a non-blank one chooses a FormID text run.</param>
/// <param name="DatabasePath">The chosen database path, before any default is applied.</param>
/// <param name="UpdateMode">The Store update behavior for the run.</param>
/// <param name="DryRun">Whether the run only produces a Processing Run Plan.</param>
internal sealed record ProcessingRunInputs(
    GameRelease? GameRelease,
    PluginListSource? PluginListSource,
    PluginListState PluginListState,
    string? FormIdListPath,
    string? DatabasePath,
    UpdateMode UpdateMode,
    bool DryRun);

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

    /// <summary>The Plugin List is still loading for the captured Plugin List Source.</summary>
    PluginListLoading,

    /// <summary>The Plugin List for the captured Plugin List Source did not load.</summary>
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
