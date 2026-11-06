#nullable enable

using System.Collections.Immutable;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers Processing Run Admission: whether a press of Process FormIDs becomes a run, which run, and why it is
///     refused when it is not (ADR-0010).
/// </summary>
/// <remarks>
///     Admission is pure, so every case calls it with plain values. The Plugin List confirmation is captured as a value
///     too: a hand-built <see cref="ConfirmedPluginList" /> wrapped in the available case, the loading or unavailable
///     singleton, or null when the Game Context holds no Plugin List Source. No live Plugin List is constructed; how each
///     Plugin List state answers its confirmation is covered by <see cref="PluginListTests" /> (ADR-0009, ADR-0010
///     2026-11-05 amendment).
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

    /// <summary>
    ///     Which Plugin List confirmation a case captures.
    /// </summary>
    /// <remarks>Public because public theory methods take it; the confirmation types themselves are internal.</remarks>
    public enum Capture
    {
        /// <summary>No confirmation: the Game Context holds no Plugin List Source.</summary>
        None,

        /// <summary>The Plugin List is still loading a Confirmed Plugin List for the captured source.</summary>
        Loading,

        /// <summary>The Plugin List cannot supply a Confirmed Plugin List for the captured source.</summary>
        Unavailable,

        /// <summary>A matching Confirmed Plugin List that selects no Plugins.</summary>
        AvailableWithoutSelection,

        /// <summary>A matching Confirmed Plugin List that selects one Plugin.</summary>
        AvailableWithSelection
    }

    [Fact]
    public void Admit_NoGameSelected_RefusesWithNoGameSelected()
    {
        var decision = ProcessingRunAdmission.Admit(
            Inputs(confirmation: null, gameRelease: null),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoGameSelected, decision);
    }

    /// <summary>
    ///     Verifies that no game selected is checked before the FormID text path chooses a run.
    /// </summary>
    [Fact]
    public void Admit_NoGameSelectedWithFormIdTextPath_RefusesWithNoGameSelected()
    {
        var decision = ProcessingRunAdmission.Admit(
            Inputs(confirmation: null, gameRelease: null, formIdListPath: FormIdListPath),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoGameSelected, decision);
    }

    /// <summary>
    ///     Verifies that a FormID text path chooses a FormID text run before any Plugin check applies, whatever the
    ///     captured confirmation: every case but the one with a selection would otherwise refuse.
    /// </summary>
    /// <param name="capture">The Plugin List confirmation captured with the press.</param>
    [Theory]
    [InlineData(Capture.None)]
    [InlineData(Capture.Loading)]
    [InlineData(Capture.Unavailable)]
    [InlineData(Capture.AvailableWithoutSelection)]
    [InlineData(Capture.AvailableWithSelection)]
    public void Admit_FormIdTextPath_AdmitsATextRunWithAnyConfirmation(Capture capture)
    {
        var decision = ProcessingRunAdmission.Admit(
            Inputs(
                Confirmation(capture),
                formIdListPath: FormIdListPath,
                updateMode: UpdateMode.ReplacePluginRecords),
            _defaultDatabaseRoot);

        var admitted = Assert.IsType<AdmittedRun>(decision);
        var request = Assert.IsType<FormIdTextProcessingRunRequest>(admitted.Request);
        Assert.Equal(FormIdListPath, request.FormIdListPath);
        Assert.Equal(DatabasePath, request.DatabasePath);
        Assert.Equal(GameRelease.SkyrimSE, request.GameRelease);
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
        var decision = ProcessingRunAdmission.Admit(
            Inputs(confirmation: null, formIdListPath: formIdListPath),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoGameDirectory, decision);
    }

    /// <summary>
    ///     Verifies that a Plugin run captured with no confirmation is refused for the missing game directory.
    /// </summary>
    /// <remarks>
    ///     The confirmation is null exactly when the Game Context holds no Plugin List Source, and the Game Context holds
    ///     a directory only when it makes a source (ADR-0011), so "no confirmation" is what "no game directory" means.
    /// </remarks>
    [Fact]
    public void Admit_PluginRunWithoutConfirmation_RefusesWithNoGameDirectory()
    {
        var decision = ProcessingRunAdmission.Admit(Inputs(confirmation: null), _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoGameDirectory, decision);
    }

    /// <summary>
    ///     Verifies that a Plugin run whose Plugin List is still loading for the captured source is refused as loading.
    /// </summary>
    [Fact]
    public void Admit_LoadingConfirmation_RefusesWithPluginListLoading()
    {
        var decision = ProcessingRunAdmission.Admit(
            Inputs(ConfirmedPluginListLoading.Instance),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.PluginListLoading, decision);
    }

    /// <summary>
    ///     Verifies that a Plugin run whose Plugin List cannot supply a confirmation for the captured source is refused as
    ///     unavailable.
    /// </summary>
    [Fact]
    public void Admit_UnavailableConfirmation_RefusesWithPluginListUnavailable()
    {
        var decision = ProcessingRunAdmission.Admit(
            Inputs(ConfirmedPluginListUnavailable.Instance),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.PluginListUnavailable, decision);
    }

    /// <summary>
    ///     Verifies that a matching Confirmed Plugin List with nothing selected is refused as no Plugins selected.
    /// </summary>
    [Fact]
    public void Admit_AvailableConfirmationWithEmptySelection_RefusesWithNoPluginsSelected()
    {
        var decision = ProcessingRunAdmission.Admit(
            Inputs(Confirmation(Capture.AvailableWithoutSelection)),
            _defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoPluginsSelected, decision);
    }

    /// <summary>
    ///     Verifies that an admitted Plugin run carries the Confirmed Plugin List's own source, takes its GameRelease from
    ///     that source, and carries the captured facts and the confirmed selection in its confirmed order.
    /// </summary>
    /// <remarks>
    ///     The confirmed source keeps the exact Data-directory spelling the Plugin List was discovered from, which is where
    ///     Plugins are read, so the ordinal comparison pins that the request carries that source and not a rebuilt one.
    /// </remarks>
    [Fact]
    public void Admit_AvailableConfirmationWithSelection_AdmitsAPluginRunCarryingTheConfirmedSourceAndSelection()
    {
        var confirmedSource = Source(_gameDirectory);
        var confirmed = ConfirmedList(
            confirmedSource,
            ["First.esp", "Second.esp", "Third.esp"],
            ["First.esp", "Third.esp"]);

        var decision = ProcessingRunAdmission.Admit(
            Inputs(new ConfirmedPluginListAvailable(confirmed), updateMode: UpdateMode.ReplacePluginRecords),
            _defaultDatabaseRoot);

        var admitted = Assert.IsType<AdmittedRun>(decision);
        var request = Assert.IsType<PluginProcessingRunRequest>(admitted.Request);
        Assert.Equal(confirmedSource, request.Source);
        Assert.Equal(confirmedSource.DataDirectory, request.Source.DataDirectory, StringComparer.Ordinal);
        Assert.Equal(GameRelease.SkyrimSE, request.GameRelease);
        Assert.Equal(["First.esp", "Third.esp"], request.PluginNames);
        Assert.Equal(DatabasePath, request.DatabasePath);
        Assert.Equal(UpdateMode.ReplacePluginRecords, request.UpdateMode);
        Assert.False(request.DryRun);
        Assert.Null(admitted.DefaultedDatabasePath);
    }

    /// <summary>
    ///     Verifies the refusal order against the last check: every Plugin refusal is reported ahead of a missing database
    ///     location, so the message shown first is the one to act on first.
    /// </summary>
    /// <param name="capture">The Plugin List confirmation captured with the press.</param>
    /// <param name="expectedReasonName">
    ///     The <see cref="RunRejectionReason" /> name, passed as a string because the enum is internal and a public test
    ///     method cannot take it as a parameter.
    /// </param>
    [Theory]
    [InlineData(Capture.None, nameof(RunRejectionReason.NoGameDirectory))]
    [InlineData(Capture.Loading, nameof(RunRejectionReason.PluginListLoading))]
    [InlineData(Capture.Unavailable, nameof(RunRejectionReason.PluginListUnavailable))]
    [InlineData(Capture.AvailableWithoutSelection, nameof(RunRejectionReason.NoPluginsSelected))]
    public void Admit_PluginRefusalWithoutDatabasePathOrRoot_RefusesWithThePluginReason(
        Capture capture,
        string expectedReasonName)
    {
        var decision = ProcessingRunAdmission.Admit(
            Inputs(Confirmation(capture), databasePath: string.Empty),
            defaultDatabaseRoot: null);

        AssertRefused(Enum.Parse<RunRejectionReason>(expectedReasonName), decision);
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
        var decision = ProcessingRunAdmission.Admit(
            Inputs(confirmation: null, GameRelease.Fallout4VR, formIdListPath: FormIdListPath,
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
        var decision = ProcessingRunAdmission.Admit(
            Inputs(confirmation: null, GameRelease.SkyrimSEGog, formIdListPath: FormIdListPath,
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
    public void Admit_PluginRunWithoutDatabasePath_DefaultsUnderTheRoot()
    {
        var decision = ProcessingRunAdmission.Admit(
            Inputs(Confirmation(Capture.AvailableWithSelection), databasePath: string.Empty),
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
        var decision = ProcessingRunAdmission.Admit(
            Inputs(confirmation: null, formIdListPath: FormIdListPath, databasePath: string.Empty, dryRun: true),
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
        var decision = ProcessingRunAdmission.Admit(
            Inputs(confirmation: null, formIdListPath: FormIdListPath, databasePath: string.Empty),
            defaultDatabaseRoot);

        AssertRefused(RunRejectionReason.NoDatabaseLocation, decision);
    }

    /// <summary>
    ///     Verifies that a selected-Plugin run that passes every Plugin check is refused when it has nowhere to write.
    /// </summary>
    [Fact]
    public void Admit_PluginRunWithoutDatabasePathOrRoot_RefusesWithNoDatabaseLocation()
    {
        var decision = ProcessingRunAdmission.Admit(
            Inputs(Confirmation(Capture.AvailableWithSelection), databasePath: string.Empty),
            defaultDatabaseRoot: null);

        AssertRefused(RunRejectionReason.NoDatabaseLocation, decision);
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
        var decision = ProcessingRunAdmission.Admit(
            Inputs(confirmation: null, formIdListPath: FormIdListPath, databasePath: databasePath, dryRun: dryRun),
            defaultDatabaseRoot: null);

        var admitted = Assert.IsType<AdmittedRun>(decision);
        Assert.Null(admitted.DefaultedDatabasePath);
        Assert.Equal(databasePath, admitted.Request.DatabasePath);
    }

    /// <summary>
    ///     Builds the Plugin List confirmation a case captures for the SkyrimSE source of the test's game directory.
    /// </summary>
    private PluginListConfirmation? Confirmation(Capture capture)
    {
        return capture switch
        {
            Capture.None => null,
            Capture.Loading => ConfirmedPluginListLoading.Instance,
            Capture.Unavailable => ConfirmedPluginListUnavailable.Instance,
            Capture.AvailableWithoutSelection => new ConfirmedPluginListAvailable(
                ConfirmedList(Source(_gameDirectory), ["User.esp"], [])),
            Capture.AvailableWithSelection => new ConfirmedPluginListAvailable(
                ConfirmedList(Source(_gameDirectory), ["User.esp"], ["User.esp"])),
            _ => throw new ArgumentOutOfRangeException(nameof(capture), capture, "Unsupported capture.")
        };
    }

    /// <summary>
    ///     Builds a Confirmed Plugin List as a plain value, as the Plugin List would hold it for the given source.
    /// </summary>
    /// <param name="source">The Plugin List Source the list was confirmed against.</param>
    /// <param name="pluginNames">Every Plugin in Plugin List order.</param>
    /// <param name="selectedPluginNames">The selected Plugins, already in Plugin List order.</param>
    private static ConfirmedPluginList ConfirmedList(
        PluginListSource source,
        string[] pluginNames,
        string[] selectedPluginNames)
    {
        return new ConfirmedPluginList(
            1,
            source,
            AdvancedMode.Off,
            [.. pluginNames.Select(static name => new PluginListEntry(name))],
            [.. selectedPluginNames]);
    }

    /// <summary>
    ///     Builds captured run inputs with a database path chosen and Dry Run off unless a test says otherwise.
    /// </summary>
    private static ProcessingRunInputs Inputs(
        PluginListConfirmation? confirmation,
        GameRelease? gameRelease = GameRelease.SkyrimSE,
        string? formIdListPath = null,
        string? databasePath = DatabasePath,
        UpdateMode updateMode = UpdateMode.Append,
        bool dryRun = false)
    {
        return new ProcessingRunInputs(
            gameRelease,
            confirmation,
            formIdListPath,
            databasePath,
            updateMode,
            dryRun);
    }

    /// <summary>
    ///     Builds the SkyrimSE Plugin List Source a Game Context would hold for the given directory.
    /// </summary>
    private static PluginListSource Source(string gameDirectory)
    {
        return PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory);
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
