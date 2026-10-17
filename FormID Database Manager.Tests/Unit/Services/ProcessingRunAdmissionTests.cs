#nullable enable

using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers Processing Run Admission: whether a press of Process FormIDs becomes a run, which run, and why it is
///     refused when it is not (ADR-0010).
/// </summary>
/// <remarks>
///     Admission is pure, so every case calls it with values. Cases that depend on a Plugin List state drive a real
///     Plugin List over the shared <see cref="ScriptedGameLoadOrders" /> fake, because only the Plugin List may construct
///     its states (ADR-0009); cases that need no Plugin List use a fresh list's No Source state.
/// </remarks>
public sealed class ProcessingRunAdmissionTests
{
    private const string DatabasePath = @"C:\Databases\formids.db";
    private const string FormIdListPath = @"C:\Lists\formids.txt";

    /// <summary>
    ///     A default database root that is deliberately never created, so a test can prove admission creates nothing.
    /// </summary>
    private readonly string _defaultDatabaseRoot = CreateUncreatedTemporaryPath();

    /// <summary>
    ///     A game directory that is never created: building a Plugin List Source from it is pure path arithmetic.
    /// </summary>
    private readonly string _gameDirectory = CreateUncreatedTemporaryPath();

    [Fact]
    public void Admit_NoGameSelected_RefusesWithNoGameSelected()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameRelease: null, gameDirectory: _gameDirectory),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoGameSelected, decision);
    }

    /// <summary>
    ///     Verifies that no game selected is checked before the FormID text path chooses a run.
    /// </summary>
    [Fact]
    public void Admit_NoGameSelectedWithFormIdTextPath_RefusesWithNoGameSelected()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameRelease: null, formIdListPath: FormIdListPath),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoGameSelected, decision);
    }

    /// <summary>
    ///     Verifies that a FormID text path chooses a FormID text run before any Plugin check applies: with no game
    ///     directory and no Plugin List source, every Plugin check would otherwise refuse.
    /// </summary>
    [Fact]
    public void Admit_FormIdTextPath_AdmitsATextRunAheadOfEveryPluginCheck()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(
                pluginList.Current,
                GameRelease.Fallout4,
                gameDirectory: null,
                formIdListPath: FormIdListPath,
                updateMode: UpdateMode.ReplacePluginRecords),
            _defaultDatabaseRoot);

        var admitted = Assert.IsType<AdmittedRun>(decision);
        var request = Assert.IsType<FormIdTextProcessingRunRequest>(admitted.Request);
        Assert.Equal(FormIdListPath, request.FormIdListPath);
        Assert.Equal(DatabasePath, request.DatabasePath);
        Assert.Equal(GameRelease.Fallout4, request.GameRelease);
        Assert.Equal(UpdateMode.ReplacePluginRecords, request.UpdateMode);
        Assert.False(request.DryRun);
        Assert.Null(admitted.DefaultedDatabasePath);
    }

    /// <summary>
    ///     Verifies that a blank FormID text path does not choose a text run, so the Plugin checks apply.
    /// </summary>
    /// <param name="formIdListPath">A FormID text path that names no file.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Admit_BlankFormIdTextPath_ChoosesAPluginRun(string? formIdListPath)
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: null, formIdListPath: formIdListPath),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoGameDirectory, decision);
    }

    /// <summary>
    ///     Verifies that a Plugin run with no game directory is refused before the Plugin List is consulted.
    /// </summary>
    /// <param name="gameDirectory">A game directory that names no directory.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Admit_PluginRunWithoutGameDirectory_RefusesWithNoGameDirectory(string? gameDirectory)
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: gameDirectory),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoGameDirectory, decision);
    }

    /// <summary>
    ///     Verifies that a malformed game directory is not turned into a refusal: no Plugin List Source can be built from
    ///     it, and the failure propagates for the caller to report as unexpected (ADR-0002).
    /// </summary>
    [Fact]
    public void Admit_MalformedGameDirectory_ThrowsArgumentException()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        Assert.ThrowsAny<ArgumentException>(() => ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: "C:\\Games\\Sky\0rim"),
            _defaultDatabaseRoot));
    }

    /// <summary>
    ///     Verifies that a Plugin List with no source, as after a cleared or retried directory, is refused as unavailable.
    /// </summary>
    [Fact]
    public void Admit_PluginListWithoutSource_RefusesWithPluginListUnavailable()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed("User.esp"));

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.PluginListUnavailable, decision);
    }

    /// <summary>
    ///     Verifies that a first discovery still in flight for the captured source is refused as loading.
    /// </summary>
    [Fact]
    public async Task Admit_PluginListLoadingForCapturedSource_RefusesWithPluginListLoading()
    {
        var held = ScriptedDiscovery.Held("User.esp");
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Sequenced(held));
        var refresh = StartRefresh(pluginList, _gameDirectory);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.PluginListLoading, decision);
        held.Release();
        await refresh;
    }

    /// <summary>
    ///     Verifies that a Plugin List refreshing a new directory after an old one was confirmed is refused as loading,
    ///     not as an empty selection: the old confirmation belongs to another source.
    /// </summary>
    [Fact]
    public async Task Admit_PluginListLoadingAfterAnotherSourceWasConfirmed_RefusesWithPluginListLoading()
    {
        var held = ScriptedDiscovery.Held("New.esp");
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Sequenced(
            ScriptedDiscovery.Completes("Old.esp"),
            held));
        await RefreshAsync(pluginList, CreateUncreatedTemporaryPath());
        SelectAll(pluginList);
        var refresh = StartRefresh(pluginList, _gameDirectory);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.PluginListLoading, decision);
        held.Release();
        await refresh;
    }

    /// <summary>
    ///     Verifies that a Plugin List confirmed for a different directory, before the captured directory's refresh has
    ///     started, is refused as loading.
    /// </summary>
    [Fact]
    public async Task Admit_PluginListReadyForAnotherDirectory_RefusesWithPluginListLoading()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed("User.esp"));
        await RefreshAsync(pluginList, CreateUncreatedTemporaryPath());
        SelectAll(pluginList);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.PluginListLoading, decision);
    }

    /// <summary>
    ///     Verifies that a Plugin List confirmed for the same directory under another GameRelease belongs to another
    ///     source, so it is refused as loading rather than reused.
    /// </summary>
    [Fact]
    public async Task Admit_PluginListReadyForAnotherGameRelease_RefusesWithPluginListLoading()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed("User.esp"));
        await RefreshAsync(pluginList, _gameDirectory);
        SelectAll(pluginList);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, GameRelease.Fallout4, _gameDirectory),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.PluginListLoading, decision);
    }

    /// <summary>
    ///     Verifies that a refresh that ended without a confirmation for the captured source is refused as unavailable.
    /// </summary>
    /// <param name="ending">How the captured source's only refresh ended.</param>
    [Theory]
    [InlineData(RefreshEnding.Failed)]
    [InlineData(RefreshEnding.Cancelled)]
    [InlineData(RefreshEnding.Faulted)]
    public async Task Admit_RefreshEndedWithoutConfirmation_RefusesWithPluginListUnavailable(RefreshEnding ending)
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Sequenced(EndingStep(ending)));
        await EndRefreshAsync(pluginList, _gameDirectory, ending);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory),
            _defaultDatabaseRoot);

        Assert.Null(pluginList.Current.Confirmed);
        AssertRefused(RunRejectionReason.PluginListUnavailable, decision);
    }

    /// <summary>
    ///     Verifies that a failed refresh for an old directory is refused as loading for the captured directory, because
    ///     its source differs from the captured one.
    /// </summary>
    [Fact]
    public async Task Admit_PluginListFailedForAnotherDirectory_RefusesWithPluginListLoading()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Sequenced(ScriptedDiscovery.Fails()));
        await RefreshAsync(pluginList, CreateUncreatedTemporaryPath());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory),
            _defaultDatabaseRoot);

        Assert.IsType<PluginListFailedState>(pluginList.Current);
        AssertRefused(RunRejectionReason.PluginListLoading, decision);
    }

    /// <summary>
    ///     Verifies that a matching Confirmed Plugin List with nothing selected is refused as no Plugins selected.
    /// </summary>
    [Fact]
    public async Task Admit_MatchingConfirmationWithEmptySelection_RefusesWithNoPluginsSelected()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed("User.esp"));
        await RefreshAsync(pluginList, _gameDirectory);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoPluginsSelected, decision);
    }

    /// <summary>
    ///     Verifies that an admitted Plugin run carries the matched Plugin List Source, takes its GameRelease from that
    ///     source, and carries the captured facts and the confirmed selection in confirmed Plugin List order rather than
    ///     the order Plugins were clicked.
    /// </summary>
    [Fact]
    public async Task Admit_MatchingConfirmationWithSelection_AdmitsAPluginRunInConfirmedOrder()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed("First.esp", "Second.esp", "Third.esp"));
        await RefreshAsync(pluginList, _gameDirectory);
        Select(pluginList, "Third.esp");
        Select(pluginList, "First.esp");

        var decision = ProcessingRunAdmission.Admit(
            Inputs(
                pluginList.Current,
                gameDirectory: _gameDirectory,
                updateMode: UpdateMode.ReplacePluginRecords),
            _defaultDatabaseRoot);

        var admitted = Assert.IsType<AdmittedRun>(decision);
        var request = Assert.IsType<PluginProcessingRunRequest>(admitted.Request);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory), request.Source);
        Assert.Equal(GameRelease.SkyrimSE, request.GameRelease);
        Assert.Equal(request.Source.GameRelease, request.GameRelease);
        Assert.Equal(["First.esp", "Third.esp"], request.PluginNames);
        Assert.Equal(DatabasePath, request.DatabasePath);
        Assert.Equal(UpdateMode.ReplacePluginRecords, request.UpdateMode);
        Assert.False(request.DryRun);
        Assert.Null(admitted.DefaultedDatabasePath);
    }

    /// <summary>
    ///     Verifies that a captured directory spelled differently from the confirmed one still matches when both name
    ///     the same canonical Data directory, and that the admitted request carries the Plugin List Source the list was
    ///     confirmed against, so its Plugins are read from exactly the Data directory they were discovered from.
    /// </summary>
    [Fact]
    public async Task Admit_EquivalentDirectorySpelling_MatchesTheConfirmationAndCarriesItsSource()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed("User.esp"));
        await RefreshAsync(pluginList, _gameDirectory);
        SelectAll(pluginList);
        var confirmedSource = Assert.IsType<ConfirmedPluginList>(pluginList.Current.Confirmed).Source;
        // Where paths compare case-insensitively the captured spelling also differs in case, so the ordinal check
        // below tells the confirmed source apart from one rebuilt from the captured directory.
        var dataDirectoryName = OperatingSystem.IsWindows() ? "data" : "Data";

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: Path.Combine(_gameDirectory, dataDirectoryName, ".")),
            _defaultDatabaseRoot);

        var request = Assert.IsType<PluginProcessingRunRequest>(Assert.IsType<AdmittedRun>(decision).Request);
        Assert.Equal(["User.esp"], request.PluginNames);
        Assert.Equal(confirmedSource, request.Source);
        Assert.Equal(confirmedSource.DataDirectory, request.Source.DataDirectory, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Verifies that a matching confirmation admits the run while the same source refreshes, using the confirmed
    ///     selection rather than waiting for the refresh.
    /// </summary>
    [Fact]
    public async Task Admit_MatchingConfirmationDuringSameSourceRefresh_AdmitsThePluginRun()
    {
        var held = ScriptedDiscovery.Held("User.esp", "New.esp");
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Sequenced(
            ScriptedDiscovery.Completes("User.esp"),
            held));
        await RefreshAsync(pluginList, _gameDirectory);
        SelectAll(pluginList);
        var refresh = StartRefresh(pluginList, _gameDirectory);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory),
            _defaultDatabaseRoot);

        Assert.IsType<PluginListRefreshingState>(pluginList.Current);
        var request = Assert.IsType<PluginProcessingRunRequest>(Assert.IsType<AdmittedRun>(decision).Request);
        Assert.Equal(["User.esp"], request.PluginNames);
        held.Release();
        await refresh;
    }

    /// <summary>
    ///     Verifies that a matching confirmation retained through a failed, cancelled or faulted same-source refresh
    ///     still admits the run.
    /// </summary>
    /// <param name="ending">How the second, same-source refresh ended.</param>
    [Theory]
    [InlineData(RefreshEnding.Failed)]
    [InlineData(RefreshEnding.Cancelled)]
    [InlineData(RefreshEnding.Faulted)]
    public async Task Admit_MatchingConfirmationRetainedThroughEndedRefresh_AdmitsThePluginRun(RefreshEnding ending)
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Sequenced(
            ScriptedDiscovery.Completes("User.esp"),
            EndingStep(ending)));
        await RefreshAsync(pluginList, _gameDirectory);
        SelectAll(pluginList);
        await EndRefreshAsync(pluginList, _gameDirectory, ending);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory),
            _defaultDatabaseRoot);

        var request = Assert.IsType<PluginProcessingRunRequest>(Assert.IsType<AdmittedRun>(decision).Request);
        Assert.Equal(["User.esp"], request.PluginNames);
    }

    /// <summary>
    ///     Verifies that a real run with no chosen database path gets one under the injected root, named after the
    ///     GameRelease's Store table, and that computing it creates nothing: the FormID Record Store creates the folder
    ///     when it opens (ADR-0001).
    /// </summary>
    /// <param name="databasePath">A captured database path that names no file.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Admit_RealRunWithoutDatabasePath_DefaultsUnderTheRootAndCreatesNothing(string? databasePath)
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, GameRelease.Fallout4VR, formIdListPath: FormIdListPath,
                databasePath: databasePath),
            _defaultDatabaseRoot);

        var admitted = Assert.IsType<AdmittedRun>(decision);
        var expected = Path.Combine(_defaultDatabaseRoot, "FormID Database Manager", "Databases", "Fallout4VR.db");
        Assert.Equal(expected, admitted.DefaultedDatabasePath);
        Assert.Equal(expected, admitted.Request.DatabasePath);
        Assert.False(Directory.Exists(_defaultDatabaseRoot),
            $"Admission must not create the default database root: {_defaultDatabaseRoot}");
    }

    /// <summary>
    ///     Verifies that a defaulted path is rooted at the injected root, never at the process's working directory or
    ///     the system directory.
    /// </summary>
    [Fact]
    public void Admit_DefaultedDatabasePath_IsUnderTheRootRatherThanTheWorkingOrSystemDirectory()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, GameRelease.SkyrimSEGog, formIdListPath: FormIdListPath,
                databasePath: string.Empty),
            _defaultDatabaseRoot);

        var databasePath = Assert.IsType<AdmittedRun>(decision).DefaultedDatabasePath;
        Assert.NotNull(databasePath);
        Assert.StartsWith(_defaultDatabaseRoot, databasePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Databases", "SkyrimSEGog.db"), databasePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(databasePath.StartsWith(Directory.GetCurrentDirectory(), StringComparison.OrdinalIgnoreCase));
        Assert.False(databasePath.StartsWith(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Verifies that an admitted Plugin run also gets the default, so the default does not depend on the run kind.
    /// </summary>
    [Fact]
    public async Task Admit_PluginRunWithoutDatabasePath_DefaultsUnderTheRoot()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed("User.esp"));
        await RefreshAsync(pluginList, _gameDirectory);
        SelectAll(pluginList);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory, databasePath: string.Empty),
            _defaultDatabaseRoot);

        var admitted = Assert.IsType<AdmittedRun>(decision);
        Assert.Equal(
            Path.Combine(_defaultDatabaseRoot, "FormID Database Manager", "Databases", "SkyrimSE.db"),
            admitted.DefaultedDatabasePath);
        Assert.Equal(admitted.DefaultedDatabasePath, admitted.Request.DatabasePath);
    }

    /// <summary>
    ///     Verifies that a Dry Run, which opens no Store, never gets a default database path chosen for it.
    /// </summary>
    [Fact]
    public void Admit_DryRunWithoutDatabasePath_GetsNoDefault()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, formIdListPath: FormIdListPath, databasePath: string.Empty, dryRun: true),
            _defaultDatabaseRoot);

        var admitted = Assert.IsType<AdmittedRun>(decision);
        Assert.Null(admitted.DefaultedDatabasePath);
        Assert.Empty(admitted.Request.DatabasePath);
        Assert.True(admitted.Request.DryRun);
    }

    /// <summary>
    ///     Verifies that a real run with neither a chosen database path nor a usable default root is refused.
    /// </summary>
    /// <param name="defaultDatabaseRoot">A default root composition could not resolve.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Admit_RealRunWithoutDatabasePathOrRoot_RefusesWithNoDatabaseLocation(string? defaultDatabaseRoot)
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, formIdListPath: FormIdListPath, databasePath: string.Empty),
            defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoDatabaseLocation, decision);
    }

    /// <summary>
    ///     Verifies that a selected-Plugin run that passes every Plugin check is refused when it has nowhere to write.
    /// </summary>
    [Fact]
    public async Task Admit_PluginRunWithoutDatabasePathOrRoot_RefusesWithNoDatabaseLocation()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed("User.esp"));
        await RefreshAsync(pluginList, _gameDirectory);
        SelectAll(pluginList);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory, databasePath: string.Empty),
            defaultDatabaseRoot: null);

        AssertRefused(RunRejectionReason.NoDatabaseLocation, decision);
    }

    /// <summary>
    ///     Verifies that an empty selection is reported ahead of a missing database location.
    /// </summary>
    [Fact]
    public async Task Admit_EmptySelectionWithoutDatabasePathOrRoot_RefusesWithNoPluginsSelected()
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed("User.esp"));
        await RefreshAsync(pluginList, _gameDirectory);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, gameDirectory: _gameDirectory, databasePath: string.Empty),
            defaultDatabaseRoot: null);

        AssertRefused(RunRejectionReason.NoPluginsSelected, decision);
    }

    /// <summary>
    ///     Verifies that a missing root does not refuse a Dry Run, which needs no database, or a run whose database path
    ///     is already chosen.
    /// </summary>
    /// <param name="dryRun">Whether the run is a Dry Run.</param>
    /// <param name="databasePath">The captured database path.</param>
    [Theory]
    [InlineData(true, "")]
    [InlineData(false, DatabasePath)]
    public void Admit_WithoutRootButNoDefaultNeeded_AdmitsTheRun(bool dryRun, string databasePath)
    {
        using var pluginList = new PluginList(ScriptedGameLoadOrders.Fixed());

        var decision = ProcessingRunAdmission.Admit(
            Inputs(pluginList.Current, formIdListPath: FormIdListPath, databasePath: databasePath, dryRun: dryRun),
            defaultDatabaseRoot: null);

        var admitted = Assert.IsType<AdmittedRun>(decision);
        Assert.Null(admitted.DefaultedDatabasePath);
        Assert.Equal(databasePath, admitted.Request.DatabasePath);
    }

    /// <summary>
    ///     How a scripted refresh ends without completing.
    /// </summary>
    public enum RefreshEnding
    {
        Failed,
        Cancelled,
        Faulted
    }

    /// <summary>
    ///     Returns the scripted discovery step that ends a refresh the given way.
    /// </summary>
    private static ScriptedDiscovery EndingStep(RefreshEnding ending)
    {
        return ending switch
        {
            RefreshEnding.Failed => ScriptedDiscovery.Fails(),
            RefreshEnding.Cancelled => ScriptedDiscovery.Cancels(),
            RefreshEnding.Faulted => ScriptedDiscovery.Faults(),
            _ => throw new ArgumentOutOfRangeException(nameof(ending), ending, "Unsupported refresh ending.")
        };
    }

    /// <summary>
    ///     Runs one refresh scripted by <see cref="EndingStep" /> and absorbs the exception that ending rethrows.
    /// </summary>
    /// <remarks>
    ///     The Plugin List publishes Cancelled only for its caller's cancellation, so a cancelled ending refreshes with a
    ///     token that is already cancelled; a faulted ending rethrows the scripted <see cref="IOException" />.
    /// </remarks>
    private static async Task EndRefreshAsync(PluginList pluginList, string gameDirectory, RefreshEnding ending)
    {
        switch (ending)
        {
            case RefreshEnding.Failed:
                await RefreshAsync(pluginList, gameDirectory);
                break;
            case RefreshEnding.Cancelled:
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pluginList.RefreshAsync(
                    GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off, new CancellationToken(true)));
                break;
            case RefreshEnding.Faulted:
                await Assert.ThrowsAsync<IOException>(() => RefreshAsync(pluginList, gameDirectory));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(ending), ending, "Unsupported refresh ending.");
        }
    }

    /// <summary>
    ///     Refreshes the Plugin List for SkyrimSE in the given directory and waits for the refresh to finish.
    /// </summary>
    private static Task RefreshAsync(PluginList pluginList, string gameDirectory)
    {
        return pluginList.RefreshAsync(GameRelease.SkyrimSE, gameDirectory, AdvancedMode.Off,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Starts a SkyrimSE refresh without waiting for it, so a held discovery keeps the Plugin List refreshing.
    /// </summary>
    private static Task StartRefresh(PluginList pluginList, string gameDirectory)
    {
        return RefreshAsync(pluginList, gameDirectory);
    }

    /// <summary>
    ///     Selects every Plugin in the current Confirmed Plugin List.
    /// </summary>
    private static void SelectAll(PluginList pluginList)
    {
        var confirmed = Assert.IsType<ConfirmedPluginList>(pluginList.Current.Confirmed);
        pluginList.Apply(new PluginSelectionForAllIntent(confirmed.MembershipVersion, true));
    }

    /// <summary>
    ///     Selects one Plugin in the current Confirmed Plugin List.
    /// </summary>
    private static void Select(PluginList pluginList, string pluginName)
    {
        var confirmed = Assert.IsType<ConfirmedPluginList>(pluginList.Current.Confirmed);
        pluginList.Apply(new PluginSelectionByNameIntent(confirmed.MembershipVersion, pluginName, true));
    }

    /// <summary>
    ///     Builds captured run inputs with a database path chosen and Dry Run off unless a test says otherwise.
    /// </summary>
    private static ProcessingRunInputs Inputs(
        PluginListState pluginListState,
        GameRelease? gameRelease = GameRelease.SkyrimSE,
        string? gameDirectory = null,
        string? formIdListPath = null,
        string? databasePath = DatabasePath,
        UpdateMode updateMode = UpdateMode.Append,
        bool dryRun = false)
    {
        return new ProcessingRunInputs(
            gameRelease,
            gameDirectory,
            pluginListState,
            formIdListPath,
            databasePath,
            updateMode,
            dryRun);
    }

    /// <summary>
    ///     Asserts that admission refused the press for exactly the expected reason.
    /// </summary>
    private static void AssertRefused(RunRejectionReason expected, RunAdmissionDecision decision)
    {
        Assert.Equal(expected, Assert.IsType<RefusedRun>(decision).Reason);
    }

    /// <summary>
    ///     Returns a unique temporary path that does not exist and is deliberately never created.
    /// </summary>
    private static string CreateUncreatedTemporaryPath()
    {
        return Path.Combine(Path.GetTempPath(), $"FormIdTests-{Guid.NewGuid():N}");
    }
}
