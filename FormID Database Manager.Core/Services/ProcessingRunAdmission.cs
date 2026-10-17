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
    /// <exception cref="ArgumentException">
    ///     A selected-Plugin run's captured game directory is malformed, so no Plugin List Source can be built from it.
    ///     An unsupported GameRelease raises the <see cref="ArgumentOutOfRangeException" /> subclass.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         The checks run in a fixed order: no GameRelease; then a non-blank FormID text path chooses a FormID text
    ///         run, to which only the database-location check still applies; then a selected-Plugin run's game
    ///         directory, its Plugin List match and its selection; and last the database location.
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
        PluginListSource? capturedSource = null;
        if (string.IsNullOrWhiteSpace(formIdListPath))
        {
            if (string.IsNullOrWhiteSpace(inputs.GameDirectory))
            {
                return new RefusedRun(RunRejectionReason.NoGameDirectory);
            }

            // Canonicalization is left to throw for a malformed directory or an unsupported release rather than being
            // turned into a refusal: neither is something the user can fix by waiting or selecting, and the caller
            // already reports an unexpected failure as one (ADR-0002).
            capturedSource = PluginListSource.Create(gameRelease, inputs.GameDirectory);
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
        // needs a path nor gets one chosen on the user's behalf.
        if (string.IsNullOrWhiteSpace(databasePath) && !inputs.DryRun)
        {
            if (string.IsNullOrWhiteSpace(defaultDatabaseRoot))
            {
                return new RefusedRun(RunRejectionReason.NoDatabaseLocation);
            }

            defaultedDatabasePath = CreateDefaultDatabasePath(gameRelease, defaultDatabaseRoot);
            databasePath = defaultedDatabasePath;
        }

        // Only a FormID text run reaches this point without a matched confirmation and its source.
        ProcessingRunRequest request = confirmed is null || capturedSource is null
            ? new FormIdTextProcessingRunRequest(
                formIdListPath,
                databasePath,
                gameRelease,
                inputs.UpdateMode,
                inputs.DryRun)
            : new PluginProcessingRunRequest(
                capturedSource.DataDirectory,
                databasePath,
                gameRelease,
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
    /// <param name="capturedSource">The Plugin List Source built from the captured Game Context.</param>
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
/// <param name="GameDirectory">The selected game root or Data directory, or null when none is selected.</param>
/// <param name="PluginListState">The Plugin List's whole immutable state snapshot at the press.</param>
/// <param name="FormIdListPath">The optional FormID text path; a non-blank one chooses a FormID text run.</param>
/// <param name="DatabasePath">The chosen database path, before any default is applied.</param>
/// <param name="UpdateMode">The Store update behavior for the run.</param>
/// <param name="DryRun">Whether the run only produces a Processing Run Plan.</param>
internal sealed record ProcessingRunInputs(
    GameRelease? GameRelease,
    string? GameDirectory,
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

    /// <summary>A selected-Plugin run has no game directory.</summary>
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
