#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.Tests.Fakes;
using FormID_Database_Manager.ViewModels;
using Moq;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class UserWorkflowTests
{
    private const string GameDirectory = @"C:\Games\Skyrim";
    private const string DatabasePath = @"C:\Databases\formids.db";
    private const string FormIdListPath = @"C:\Lists\formids.txt";

    /// <summary>
    /// The refusal sentence for a Plugin List still loading for the captured directory, pinned by
    /// <see cref="ProcessingRunPresentationTests" />.
    /// </summary>
    private const string PluginListLoadingMessage =
        "The plugin list is still loading for this directory. Try again when it finishes.";

    /// <summary>
    /// The default database root injected into every workflow under test. It is deliberately never created, so a
    /// test can prove that defaulting a database path leaves nothing on disk, and no test touches the developer's
    /// own local application data directory.
    /// </summary>
    private readonly string _defaultDatabaseRoot =
        Path.Combine(Path.GetTempPath(), $"FormIdTests-DefaultRoot-{Guid.NewGuid():N}");

    private readonly SynchronousThreadDispatcher _dispatcher = new();
    private readonly Mock<IFileDialogService> _fileDialogService = new();
    private readonly GameInstallations _gameInstallations;
    private readonly InMemoryGameInstallationProbe _gameInstallationProbe = new();
    private readonly PluginList _pluginList;
    private readonly RecordingPluginListGameLoadOrders _pluginListGameLoadOrders;
    private readonly RecordingProcessingRunExecutor _processingRunExecutor;
    private readonly List<ProcessingRunRequest> _processingRuns = [];
    private readonly List<PluginListSource> _refreshes = [];
    private readonly MainWindowViewModel _viewModel;

    public UserWorkflowTests()
    {
        _viewModel = new MainWindowViewModel(_dispatcher);
        // Game Installation resolution has no interface to mock: the real detection and location members run against
        // whatever layout and install records a test declares on the probe.
        _gameInstallations = new GameInstallations(_gameInstallationProbe);
        _pluginListGameLoadOrders = new RecordingPluginListGameLoadOrders(_refreshes);
        _pluginList = new PluginList(_pluginListGameLoadOrders);
        _processingRunExecutor = new RecordingProcessingRunExecutor(_processingRuns);
    }

    /// <summary>
    /// Verifies that the User Workflow forwards Browse to the Game Context it composed, which then detects, projects
    /// and refreshes. What each transition does is covered by <see cref="GameContextTests" />.
    /// </summary>
    [Fact]
    public async Task BrowseGameDirectoryAsync_DirectoryWithoutGameRelease_ForwardsToTheGameContext()
    {
        const string browsedDirectory = @"E:\Games\Fallout4";
        _gameInstallationProbe.WithFile(Path.Combine(browsedDirectory, "Data", "Fallout4.esm"));
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(browsedDirectory));
        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Equal(browsedDirectory, _viewModel.GameDirectory);
        Assert.Equal([PluginListSource.Create(GameRelease.Fallout4, browsedDirectory)], _refreshes);
    }

    /// <summary>
    /// Verifies that the User Workflow forwards a release selection to the Game Context it composed, which then
    /// projects the first installed location and refreshes. What each transition does is covered by
    /// <see cref="GameContextTests" />.
    /// </summary>
    [Fact]
    public async Task SelectGameReleaseAsync_InstalledRelease_ForwardsToTheGameContext()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut();

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal([PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory)], _refreshes);
    }

    /// <summary>
    /// Verifies that the User Workflow forwards a detected-directory pick to the Game Context it composed, which then
    /// projects and refreshes. What each transition does is covered by <see cref="GameContextTests" />.
    /// </summary>
    [Fact]
    public async Task SelectDetectedDirectoryAsync_SecondDetectedDirectory_ForwardsToTheGameContext()
    {
        const string secondDirectory = @"D:\Games\Skyrim";
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory, secondDirectory);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        await sut.SelectDetectedDirectoryAsync(secondDirectory);

        Assert.Equal(secondDirectory, _viewModel.GameDirectory);
        Assert.Equal(
            [
                PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
                PluginListSource.Create(GameRelease.SkyrimSE, secondDirectory)
            ],
            _refreshes);
    }

    /// <summary>
    /// Verifies that the User Workflow forwards an Advanced Mode change to the Game Context it composed, which then
    /// projects and refreshes the current source. What each transition does is covered by
    /// <see cref="GameContextTests" />.
    /// </summary>
    [Fact]
    public async Task SetAdvancedModeAsync_On_ForwardsToTheGameContext()
    {
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        await sut.SetAdvancedModeAsync(AdvancedMode.On);

        Assert.True(_viewModel.AdvancedMode);
        Assert.Equal(
            [
                PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
                PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory)
            ],
            _refreshes);
        Assert.Equal(
            AdvancedMode.On,
            Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed).AdvancedMode);
    }

    [Fact]
    public async Task SelectDatabaseAsync_PickerFailure_LeavesStateUnchangedAndRecordsWorkflowError()
    {
        _viewModel.DatabasePath = DatabasePath;
        _fileDialogService.Setup(x => x.SelectDatabaseFile())
            .ReturnsAsync(FileDialogResult.Failure("picker unavailable"));

        var sut = CreateSut();

        await sut.SelectDatabaseAsync();

        Assert.Equal(DatabasePath, _viewModel.DatabasePath);
        Assert.Contains("Error selecting database: picker unavailable", _viewModel.ErrorMessages);
    }

    /// <summary>
    /// Verifies where a Processing Run Admission refusal is shown: exactly one rendered error, with no run activity at
    /// any point and no call to the executor. Each refusal reason and their order are covered by
    /// <see cref="ProcessingRunAdmissionTests" />.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_Refused_AddsOneRenderedErrorWithoutRunActivity()
    {
        var sut = CreateSut();
        var projectedStatuses = RecordProjectedStatuses();
        var progressShown = false;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsProgressVisible) && _viewModel.IsProgressVisible)
            {
                progressShown = true;
            }
        };

        await sut.ProcessFormIdsAsync();

        Assert.Equal(["Please select a game from the dropdown first."], _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
        Assert.Equal(0, _processingRunExecutor.CancelCallCount);
        Assert.DoesNotContain("Initializing...", projectedStatuses);
        Assert.Empty(projectedStatuses);
        Assert.False(progressShown);
        Assert.False(_viewModel.IsProgressVisible);
        Assert.Equal("Process FormIDs", _viewModel.ProcessButtonText);
    }

    /// <summary>
    /// Verifies that a refused press clears stale errors and warnings, so the refusal is the only error shown.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_Refused_ClearsStaleErrorsAndWarnings()
    {
        var sut = CreateSut();
        _viewModel.ErrorMessages.Add("Stale error");
        _viewModel.WarningMessages.Add("Stale warning");

        await sut.ProcessFormIdsAsync();

        Assert.Equal(["Please select a game from the dropdown first."], _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
    }

    /// <summary>
    /// Verifies that a refusal never leaves the workflow believing a run is active: the next press starts a run
    /// rather than requesting cancellation.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_PressAfterRefusal_StartsARunRatherThanCancelling()
    {
        var sut = CreateSut();
        await sut.ProcessFormIdsAsync();
        await ConfigureValidPluginProcessingRunAsync(sut);

        await sut.ProcessFormIdsAsync();

        Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(0, _processingRunExecutor.CancelCallCount);
    }

    /// <summary>
    /// Verifies that the press captures the Game Context's Plugin List Source: with a GameRelease selected but no
    /// source held, the press is refused for the missing directory through Processing Run Presentation, and the run
    /// gate stays free for the next press.
    /// </summary>
    /// <remarks>
    /// Which directories the Game Context refuses to hold is covered by <see cref="GameContextTests" />, and the
    /// refusal reason by <see cref="ProcessingRunAdmissionTests" />. This pins only that the workflow hands admission
    /// the Game Context's source, so no directory problem can reach the user as "Error processing FormIDs".
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_GameContextWithoutSource_RefusesForTheMissingDirectoryAndLeavesTheGateFree()
    {
        _viewModel.DatabasePath = DatabasePath;
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.Null(_pluginList.Current.Confirmed);
        _viewModel.WarningMessages.Add("Stale warning");
        var projectedStatuses = RecordProjectedStatuses();

        await sut.ProcessFormIdsAsync();

        Assert.Equal(["Game directory must be specified when processing plugins"], _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Empty(_processingRuns);
        Assert.Empty(projectedStatuses);
        Assert.Equal("Process FormIDs", _viewModel.ProcessButtonText);

        _viewModel.FormIdListPath = FormIdListPath;
        await sut.ProcessFormIdsAsync();

        Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(0, _processingRunExecutor.CancelCallCount);
    }

    /// <summary>
    /// Verifies the whole path through the composed modules once: Browsing to a game directory detects its
    /// GameRelease, the Plugin List loads for the Game Context's source, and a press with a Plugin selected is admitted
    /// as a run over exactly that source.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_AfterBrowsingAndLoadingThePluginList_AdmitsARunOverThePickedSource()
    {
        const string browsedDirectory = @"E:\Games\Fallout4";
        _gameInstallationProbe.WithFile(Path.Combine(browsedDirectory, "Data", "Fallout4.esm"));
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(browsedDirectory));
        _pluginListGameLoadOrders.PluginNames = ["First.esp", "Second.esp"];
        _viewModel.DatabasePath = DatabasePath;
        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();
        var confirmed = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Second.esp", true);
        await sut.ProcessFormIdsAsync();

        Assert.Empty(_viewModel.ErrorMessages);
        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(PluginListSource.Create(GameRelease.Fallout4, browsedDirectory), run.Source);
        Assert.Equal(GameRelease.Fallout4, run.GameRelease);
        Assert.Equal(["Second.esp"], run.PluginNames);
        Assert.Equal(DatabasePath, run.DatabasePath);
    }

    /// <summary>
    /// Verifies that text-run admission and default Store naming use authority even while presentation is still queued.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_FormIdTextFile_UsesAuthoritativeReleaseBeforeQueuedProjection()
    {
        var queuedActions = new Queue<Action>();
        var dispatcher = new QueuedThreadDispatcher(
            () => false,
            action =>
            {
                queuedActions.Enqueue(action);
                return true;
            },
            "Dispatcher rejected test work.");
        var viewModel = new MainWindowViewModel(dispatcher)
        {
            FormIdListPath = FormIdListPath
        };
        var pluginList = new PluginList(_pluginListGameLoadOrders);
        var processingRuns = new List<ProcessingRunRequest>();
        var executor = new RecordingProcessingRunExecutor(processingRuns);
        using var sut = new UserWorkflow(
            viewModel,
            dispatcher,
            _fileDialogService.Object,
            _gameInstallations,
            pluginList,
            executor,
            _defaultDatabaseRoot);

        await sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        Assert.Null(viewModel.SelectedGame);
        Assert.NotEmpty(queuedActions);

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(processingRuns));
        Assert.Equal(GameRelease.Fallout4, run.GameRelease);
        Assert.Equal("Fallout4.db", Path.GetFileName(run.DatabasePath));
        Assert.Equal(run.DatabasePath, viewModel.DatabasePath);
    }

    /// <summary>
    /// Verifies that a real run with no chosen database path defaults one under the injected root, writes it back to
    /// the database box, and creates nothing: the FormID Record Store creates the folder when the run opens it.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_EmptyDatabasePath_DefaultsUnderInjectedRootWithoutCreatingIt()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
        _viewModel.DatabasePath = string.Empty;

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(
            Path.Combine(_defaultDatabaseRoot, "FormID Database Manager", "Databases", "SkyrimSE.db"),
            _viewModel.DatabasePath);
        Assert.Equal(_viewModel.DatabasePath, run.DatabasePath);
        Assert.Equal(["User.esp"], run.PluginNames);
        Assert.False(Directory.Exists(_defaultDatabaseRoot),
            $"Defaulting a database path must not create the root: {_defaultDatabaseRoot}");
    }

    /// <summary>
    /// Verifies that a Plugin Processing Run captures one confirmed source and ordered selection snapshot.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_PluginRun_CapturesCanonicalConfirmedPluginListFactsInOrder()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["First.esp", "Second.esp", "Third.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Third.esp", true);
        sut.SetPluginSelection(confirmed.MembershipVersion, "First.esp", true);

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(GameRelease.SkyrimSE, run.GameRelease);
        Assert.Equal(confirmed.Source, run.Source);
        Assert.Equal(["First.esp", "Third.esp"], run.PluginNames);
    }

    /// <summary>
    /// Verifies that later same-source refresh and selection publication cannot mutate an already captured run request.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DuringSameSourceRefresh_CapturedRequestRemainsImmutable()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["First.esp", "Second.esp", "Third.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "First.esp", true);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Third.esp", true);

        var refreshCompletion = new TaskCompletionSource<AvailablePluginsDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListGameLoadOrders.Handler = (_, _) => refreshCompletion.Task;
        var refresh = sut.SetAdvancedModeAsync(AdvancedMode.On);

        await sut.ProcessFormIdsAsync();
        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));

        refreshCompletion.SetResult(PluginListGameLoadOrdersStub.Discovered(
            ["Third.esp", "Second.esp", "First.esp", "New.esp"]));
        await refresh;
        var refreshed = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        sut.SetPluginSelection(refreshed.MembershipVersion, "Second.esp", true);

        Assert.Equal(confirmed.Source.GameRelease, run.GameRelease);
        Assert.Equal(confirmed.Source, run.Source);
        Assert.Equal(["First.esp", "Third.esp"], run.PluginNames);
    }

    /// <summary>
    /// Verifies that a different-source transition invalidates old selection before a run can be admitted, and that the
    /// press is refused as the Plugin List still loading rather than as an empty selection.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DuringSourceTransition_DoesNotMixOldSelectionWithNewGameContext()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["Old.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Old.esp", true);

        var refreshCompletion = new TaskCompletionSource<AvailablePluginsDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListGameLoadOrders.Handler = (_, _) => refreshCompletion.Task;
        var transition = sut.SelectDetectedDirectoryAsync(@"D:\Games\Fallout4");
        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(@"D:\Games\Fallout4", _viewModel.GameDirectory);
        Assert.Null(_pluginList.Current.Confirmed);

        await sut.ProcessFormIdsAsync();

        Assert.Empty(_processingRuns);
        Assert.Equal([PluginListLoadingMessage], _viewModel.ErrorMessages);

        refreshCompletion.SetResult(PluginListGameLoadOrdersStub.Discovered(["New.esp"]));
        await transition;
    }

    /// <summary>
    /// Verifies that a run started reentrantly from release projection cannot reuse the previous source confirmation,
    /// and that the refused press writes no default database path for the newly projected release.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DuringReleaseProjection_UsesCapturedAuthoritativeContext()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["Old.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Old.esp", true);
        _viewModel.DatabasePath = string.Empty;
        Task? reentrantRun = null;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.SelectedGame))
            {
                reentrantRun = sut.ProcessFormIdsAsync();
            }
        };

        await sut.SelectGameReleaseAsync(GameRelease.Fallout4);
        Assert.NotNull(reentrantRun);
        await reentrantRun;

        Assert.Empty(_processingRuns);
        Assert.Contains("Game directory must be specified when processing plugins", _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.DatabasePath);
    }

    /// <summary>
    /// Verifies that a run started reentrantly from directory projection rejects confirmation for the previous source.
    /// The press lands before the new directory's refresh starts, so the Plugin List still describes the old source and
    /// the truthful refusal is that it is still loading for this directory.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DuringDirectoryProjection_RejectsMismatchedConfirmation()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["Old.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Old.esp", true);
        _viewModel.DatabasePath = DatabasePath;
        var refreshCompletion = new TaskCompletionSource<AvailablePluginsDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListGameLoadOrders.Handler = (_, _) => refreshCompletion.Task;
        Task? reentrantRun = null;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.GameDirectory))
            {
                reentrantRun = sut.ProcessFormIdsAsync();
            }
        };

        var transition = sut.SelectDetectedDirectoryAsync(@"D:\Games\Skyrim");
        Assert.NotNull(reentrantRun);
        await reentrantRun;

        Assert.Empty(_processingRuns);
        Assert.Equal([PluginListLoadingMessage], _viewModel.ErrorMessages);

        refreshCompletion.SetResult(PluginListGameLoadOrdersStub.Discovered(["New.esp"]));
        await transition;
    }

    /// <summary>
    /// Verifies that startup notifications cannot change the accepted run, but edits remain available to the next run.
    /// </summary>
    /// <param name="notification">The startup notification that edits the run inputs.</param>
    [Theory]
    [InlineData("Initializing")]
    [InlineData("Errors")]
    [InlineData("Warnings")]
    public async Task ProcessFormIdsAsync_StartupNotificationEditsInputs_UsesCapturedInputsUntilNextRun(
        string notification)
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        var edited = false;

        void EditInputs()
        {
            if (edited)
            {
                return;
            }

            edited = true;
            sut.SelectNoPlugins();
            _viewModel.FormIdListPath = FormIdListPath;
            _viewModel.DatabasePath = @"C:\Databases\next.db";
            _viewModel.UpdateMode = true;
            _viewModel.DryRun = true;
        }

        if (notification == "Initializing")
        {
            OnRunStatus("Initializing...", EditInputs);
        }
        else if (notification == "Errors")
        {
            _viewModel.ErrorMessages.CollectionChanged += (_, _) => EditInputs();
        }
        else
        {
            _viewModel.WarningMessages.CollectionChanged += (_, _) => EditInputs();
        }

        await sut.ProcessFormIdsAsync();

        Assert.True(edited);
        var first = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(["User.esp"], first.PluginNames);
        Assert.Equal(DatabasePath, first.DatabasePath);
        Assert.Equal(UpdateMode.Append, first.UpdateMode);
        Assert.False(first.DryRun);

        await sut.ProcessFormIdsAsync();

        Assert.Equal(2, _processingRuns.Count);
        var next = Assert.IsType<FormIdTextProcessingRunRequest>(_processingRuns[1]);
        Assert.Equal(FormIdListPath, next.FormIdListPath);
        Assert.Equal(@"C:\Databases\next.db", next.DatabasePath);
        Assert.Equal(UpdateMode.ReplacePluginRecords, next.UpdateMode);
        Assert.True(next.DryRun);
    }

    /// <summary>
    /// Verifies that a release change during initialization cannot invalidate the selection already accepted for a run.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_InitializingChangesGameContext_UsesCapturedSourceAndSelection()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        OnRunStatus("Initializing...", () => sut.SelectGameReleaseAsync(null).GetAwaiter().GetResult());

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(GameRelease.SkyrimSE, run.GameRelease);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory), run.Source);
        Assert.Equal(["User.esp"], run.PluginNames);
        Assert.Null(_viewModel.SelectedGame);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    /// <summary>
    /// Verifies that text-run input capture preserves both its path and Dry Run decision across initialization.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_InitializingEditsTextDryRun_UsesCapturedPathWithoutDefaulting()
    {
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _viewModel.FormIdListPath = FormIdListPath;
        _viewModel.DryRun = true;
        OnRunStatus("Initializing...", () =>
        {
            _viewModel.FormIdListPath = @"C:\Lists\next.txt";
            _viewModel.DryRun = false;
        });

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(FormIdListPath, run.FormIdListPath);
        Assert.True(run.DryRun);
        Assert.Empty(run.DatabasePath);
        Assert.Empty(_viewModel.DatabasePath);
    }

    /// <summary>
    /// Verifies that default-path publication cannot alter the modes used to create either kind of accepted request.
    /// </summary>
    /// <param name="textRun">Whether the accepted run imports a FormID text file.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessFormIdsAsync_DefaultPathNotificationEditsModes_UsesCapturedModes(bool textRun)
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _viewModel.DatabasePath = string.Empty;
        _viewModel.FormIdListPath = textRun ? FormIdListPath : string.Empty;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.DatabasePath))
            {
                _viewModel.UpdateMode = true;
                _viewModel.DryRun = true;
            }
        };

        await sut.ProcessFormIdsAsync();

        var run = Assert.Single(_processingRuns);
        Assert.Equal(textRun, run is FormIdTextProcessingRunRequest);
        Assert.Equal(UpdateMode.Append, run.UpdateMode);
        Assert.False(run.DryRun);
        Assert.Equal("SkyrimSE.db", Path.GetFileName(run.DatabasePath));
        Assert.Equal(run.DatabasePath, _viewModel.DatabasePath);
    }

    /// <summary>
    /// Verifies that a default chosen for the admitted run does not overwrite a database-path edit made during a
    /// startup notification, while the run itself still uses the default.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_InitializingEditsEmptyDatabasePath_PreservesEdit()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
        OnRunStatus("Initializing...", () => _viewModel.DatabasePath = DatabasePath);

        await sut.ProcessFormIdsAsync();

        Assert.Equal(DatabasePath, _viewModel.DatabasePath);
        Assert.Equal("SkyrimSE.db", Path.GetFileName(Assert.Single(_processingRuns).DatabasePath));
    }

    /// <summary>
    /// Verifies that a refused press writes no default database path: only an admitted run gets one.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_RefusedWithEmptyDatabasePath_WritesNoDefaultPath()
    {
        var sut = CreateSut();
        await ConfirmPluginListAsync(sut, ["User.esp"]);

        await sut.ProcessFormIdsAsync();

        Assert.Empty(_processingRuns);
        Assert.Empty(_viewModel.DatabasePath);
        Assert.Equal(["No plugins selected"], _viewModel.ErrorMessages);
    }

    /// <summary>
    /// Verifies that a second press during the first startup notification requests cancellation without accepting another run.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_PressedDuringInitializing_RequestsCancellationWithoutSecondRun()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        OnRunStatus("Initializing...", () => sut.ProcessFormIdsAsync().GetAwaiter().GetResult());

        await sut.ProcessFormIdsAsync();

        Assert.Single(_processingRuns);
        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_UpdateModeOn_CreatesReplaceModeRunRequest()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _viewModel.UpdateMode = true;

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(UpdateMode.ReplacePluginRecords, run.UpdateMode);
    }

    /// <summary>
    ///     Verifies that the Dry Run toggle reaches the run request, for both request kinds.
    /// </summary>
    /// <remarks>
    ///     Issue #67. The toggle is what makes a dry run reachable at all: without it the plan a run can produce is
    ///     something no user can ask for.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_DryRunOn_CreatesADryRunRequest()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _viewModel.DryRun = true;

        await sut.ProcessFormIdsAsync();

        Assert.True(Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns)).DryRun);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_DryRunOnWithAFormIdTextFile_CreatesADryRunRequest()
    {
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _viewModel.FormIdListPath = @"C:\Imports\formids.txt";
        _viewModel.DryRun = true;

        await sut.ProcessFormIdsAsync();

        Assert.True(Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(_processingRuns)).DryRun);
    }

    /// <summary>
    ///     Verifies that a dry run leaves an empty database path empty.
    /// </summary>
    /// <remarks>
    ///     A dry run opens no FormID Record Store, so defaulting a path for it would leave one the user never chose in
    ///     the box after a run that deliberately wrote nothing.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_DryRunWithEmptyDatabasePath_DoesNotChooseADefaultPath()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
        _viewModel.DatabasePath = string.Empty;
        _viewModel.DryRun = true;

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Empty(_viewModel.DatabasePath);
        Assert.Empty(run.DatabasePath);
        Assert.False(Directory.Exists(_defaultDatabaseRoot));
    }

    /// <summary>
    ///     Verifies the workflow writes a dry run's plan to the information messages the user reads.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DryRunOutcome_AddsTheRenderedPlanToTheInformationMessages()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _viewModel.DryRun = true;
        _processingRunExecutor.Outcome = new PlannedRunOutcome(new PluginRunPlan(new PluginIngestionPlan(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, @"C:\Games\Skyrim"),
                string.Empty,
                ["User.esp", "Absent.esp"], UpdateMode.Append, dryRun: true),
            [
            new PlannedPluginIngestion("User.esp"),
            new PlannedPluginSkip("Absent.esp", PlannedSkipReason.NotPresentInLoadOrder)
        ])));

        await sut.ProcessFormIdsAsync();

        // The last message rather than the only one: confirming a Plugin List already reported what it loaded, and a
        // run does not clear the information list the way it clears warnings and errors.
        Assert.Equal(
            "Dry run: 1 would be ingested, 1 would be skipped, and 0 would fail." + Environment.NewLine +
            "Would ingest User.esp" + Environment.NewLine +
            "Would skip Absent.esp: not present in the load order",
            _viewModel.InformationMessages[^1]);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_StartsNewRun_ClearsStaleWarnings()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _viewModel.WarningMessages.Add("Stale warning");

        await sut.ProcessFormIdsAsync();

        Assert.Empty(_viewModel.WarningMessages);
    }

    /// <summary>
    ///     Verifies the workflow performs the writes a warned run's rendered report implies, and no others.
    /// </summary>
    /// <remarks>
    ///     A run no longer says a word about how it ended: the executor returns the outcome and the workflow renders
    ///     it, so this asserts the writes rather than the routing of a warning event that no longer exists.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_RunOutcomeWithASkippedPlugin_AddsTheRenderedWarningMessage()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.Outcome = CreatePluginRunOutcome(
            new SkippedPlugin("User.esp", SkippedPluginReason.ZeroFormIdRecords));

        await sut.ProcessFormIdsAsync();

        Assert.Equal(
            [
                "1 processing warning." + Environment.NewLine +
                "User.esp: User.esp produced zero FormID records."
            ],
            _viewModel.WarningMessages);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    /// <summary>
    ///     Verifies the workflow performs the writes a failed run's rendered report implies, and no others.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_RunOutcomeWithAFailedPlugin_AddsTheRenderedErrorMessage()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.Outcome = CreatePluginRunOutcome(
            new FailedPlugin(
                "User.esp",
                new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")));

        await sut.ProcessFormIdsAsync();

        Assert.Equal(
            [
                "1 failed plugin." + Environment.NewLine +
                "User.esp: Error opening User.esp: Invalid plugin header."
            ],
            _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
    }

    /// <summary>
    ///     Verifies a run stopped by an unresolvable master reaches the user as the failure's own message, without the
    ///     generic processing-error prefix that every other terminal failure gets.
    /// </summary>
    /// <remarks>
    ///     Issue #52. The message already names the missing master and what to do about it, so burying it behind
    ///     "Error processing FormIDs" would waste the one piece of information the fix exists to deliver (ADR-0006).
    ///     The generic case below is asserted alongside it so this stays a distinction rather than a coincidence.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_UnresolvableMaster_ShowsTheFailureMessageUnwrapped()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        var failure = new UnresolvableMasterException("User.esp", "Starfield.esm");
        // The real executor reports this status for any terminal failure before rethrowing, so it is reproduced here:
        // what the assertion needs to show is that it stays out of the error list rather than doubling the message.
        _processingRunExecutor.ProgressToReport.Add(
            new ProcessingRunFailure(failure.Message));
        _processingRunExecutor.ExecuteFailure = failure;
        // Recorded after the setup that confirmed the Plugin List, so the sequence below is the run's alone.
        var projectedStatuses = RecordProjectedStatuses();

        await sut.ProcessFormIdsAsync();

        Assert.Equal([failure.Message], _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.False(_viewModel.IsProgressVisible);
        // Cleared by the workflow's own finally, so the prefixed status is transient and never the lasting report.
        Assert.Equal(string.Empty, _viewModel.ProgressStatus);
        // The complete sequence rather than a containment check: the prefixed status appearing once on the transient
        // channel and nowhere else is the whole distinction this test draws.
        Assert.Equal(
            ["Initializing...", $"Error during processing: {failure.Message}", string.Empty],
            projectedStatuses);
    }

    /// <summary>
    ///     Verifies where each part of a rendered run report lands when one run produced both: the warning message in
    ///     the warning list, the failure message in the error list, and the completion status only on the transient
    ///     progress channel.
    /// </summary>
    /// <remarks>
    ///     The separate warned-run and failed-run tests above cannot show this, because each has only one part to
    ///     place. Routing is the workflow's own responsibility — the wording of all three parts is pinned by
    ///     <see cref="ProcessingRunPresentationTests" /> — so what is asserted here is which list each part reached,
    ///     and that the completion status reached none of them.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_RunOutcomeWithWarningsAndFailures_RoutesEachRenderedPartToItsOwnList()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.Outcome = CreatePluginRunOutcome(
            new IngestedPlugin("User.esp", 1, new ProcessingWarning(1, ["Recoverable issue"])),
            new FailedPlugin(
                "Bad.esp",
                new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")));
        var projectedStatuses = RecordProjectedStatuses();

        await sut.ProcessFormIdsAsync();

        Assert.Equal(
            [
                "1 processing warning." + Environment.NewLine +
                "User.esp: 1 recoverable record issue. Recoverable issue"
            ],
            _viewModel.WarningMessages);
        Assert.Equal(
            [
                "1 failed plugin." + Environment.NewLine +
                "Bad.esp: Error opening Bad.esp: Invalid plugin header."
            ],
            _viewModel.ErrorMessages);
        Assert.Equal(
            [
                "Initializing...",
                "Processing completed with failures: 1 ingested, 0 skipped, and 1 failed Plugins.",
                string.Empty
            ],
            projectedStatuses);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_UnexpectedRunFailure_ShowsTheGenericProcessingErrorMessage()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.ExecuteFailure = new InvalidOperationException("store unavailable");

        await sut.ProcessFormIdsAsync();

        Assert.Equal(["Error processing FormIDs: store unavailable"], _viewModel.ErrorMessages);
        Assert.False(_viewModel.IsProgressVisible);
    }

    /// <summary>
    ///     Verifies a press arriving while a run is in flight cancels that run instead of starting a second one, and
    ///     that the acknowledgement reaches the progress channel while the run is still the activity that owns it.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_PressedWhileRunActive_CancelsThatRunAndShowsCancellingStatus()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.ProgressToReport.Add(new IngestingPlugin("User.esp", 2, 5));
        var statusDuringCancellation = string.Empty;
        var buttonTextDuringRun = string.Empty;
        // The run's own progress report notifies synchronously while the run is still in flight, which is how this
        // test acts mid-run without the recording executor needing a gate.
        OnRunStatus("Ingesting plugin 2 of 5: User.esp", () =>
        {
            buttonTextDuringRun = _viewModel.ProcessButtonText;
            // The second press: the workflow's own run state, not the ViewModel, decides this means cancel.
            sut.ProcessFormIdsAsync().GetAwaiter().GetResult();
            statusDuringCancellation = _viewModel.ProgressStatus;
        });

        await sut.ProcessFormIdsAsync();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.Single(_processingRuns);
        Assert.Equal("Cancel Processing", buttonTextDuringRun);
        Assert.Equal("Cancelling...", statusDuringCancellation);
        Assert.Equal("Process FormIDs", _viewModel.ProcessButtonText);
    }

    /// <summary>
    ///     Verifies that cancelling an active Processing Run leaves a lasting acknowledgement in the information
    ///     messages, and that the acknowledgement never reaches the transient progress channel at all.
    /// </summary>
    /// <remarks>
    ///     Issue #60. The notice used to be written to the progress channel by this workflow's cancellation handler and
    ///     then erased by the run's own cleanup one step later, so the user never saw it and nothing covered the case.
    ///     Both halves are asserted here: that it lands in the message lists where the other terminal facts about a run
    ///     go, and that it survives the cleanup that used to erase it.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_CancelledMidRun_PutsTheAcknowledgementInTheInformationMessages()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.ProgressToReport.Add(new IngestingPlugin("User.esp", 2, 5));
        var projectedStatuses = RecordProjectedStatuses();
        // The second press is the cancellation intent, delivered mid-run from the run's own progress notification.
        OnRunStatus("Ingesting plugin 2 of 5: User.esp", () =>
        {
            sut.ProcessFormIdsAsync().GetAwaiter().GetResult();
            // Armed only once the press has reached the executor, so the run ends as cancelled because it was
            // cancelled: the real executor returns this outcome for the cancellation it accepted, and never for one
            // it was not asked for.
            _processingRunExecutor.Outcome = new CancelledRunOutcome();
        });

        await sut.ProcessFormIdsAsync();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        // The Plugin List refresh that set this run up leaves its own information message ahead of the acknowledgement.
        Assert.Contains("Processing cancelled by user.", _viewModel.InformationMessages);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
        // The run's cleanup hands the channel back, and can no longer erase an acknowledgement that was never on it.
        Assert.Equal(string.Empty, _viewModel.ProgressStatus);
        Assert.False(_viewModel.IsProgressVisible);
        // The complete sequence rather than a containment check: showing that the acknowledgement never reached a
        // channel that erases itself means stating everything that did reach it, not just the one string it did not.
        Assert.Equal(
            ["Initializing...", "Ingesting plugin 2 of 5: User.esp", "Cancelling...", string.Empty],
            projectedStatuses);
    }

    /// <summary>
    ///     Verifies the window this workflow owns run state to close: a press landing after the run was admitted but
    ///     before the executor has been handed the run cancels rather than launching a second run.
    /// </summary>
    /// <remarks>
    ///     The second press is delivered from the notification raised when the workflow defaults the database path,
    ///     which happens inside that window. The executor's cancellation source does not exist yet at that point, so
    ///     an executor-owned run flag could not have answered this press correctly.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_PressedBeforeTheRunReachesTheExecutor_DoesNotLaunchASecondRun()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
        var secondPressed = false;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainWindowViewModel.DatabasePath) || secondPressed)
            {
                return;
            }

            secondPressed = true;
            sut.ProcessFormIdsAsync().GetAwaiter().GetResult();
        };

        await sut.ProcessFormIdsAsync();

        Assert.True(secondPressed);
        Assert.Single(_processingRuns);
        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
    }

    /// <summary>
    ///     Verifies the collision this projection exists to arbitrate: a Plugin List refresh started during a
    ///     Processing Run never takes the progress channel off the run, and the run's report survives the refresh.
    /// </summary>
    /// <param name="refreshTrigger">The user input used to trigger the mid-run refresh.</param>
    [Theory]
    [InlineData(RefreshTrigger.AdvancedMode)]
    [InlineData(RefreshTrigger.GameRelease)]
    [InlineData(RefreshTrigger.GameDirectory)]
    public async Task ProcessFormIdsAsync_PluginListRefreshDuringRun_KeepsTheRunOnTheProgressChannel(
        RefreshTrigger refreshTrigger)
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Games\Fallout4");
        // A different directory, because Browse to the current source is a no-op while its Plugin List is confirmed
        // and so would start no refresh to collide with the run.
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(@"D:\Games\Skyrim"));
        _processingRunExecutor.ProgressToReport.Add(new IngestingPlugin("User.esp", 2, 5));
        _processingRunExecutor.Outcome = new PlannedRunOutcome(new PluginRunPlan(new PluginIngestionPlan(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
                string.Empty,
                ["User.esp"], UpdateMode.Append, dryRun: true),
            [new PlannedPluginIngestion("User.esp")])));
        var projectedStatuses = RecordProjectedStatuses();
        var statusAfterRefresh = string.Empty;
        var valueAfterRefresh = 0d;
        // The refresh is triggered from the run's own progress notification, so it genuinely overlaps a run in flight.
        OnRunStatus("Ingesting plugin 2 of 5: User.esp", () =>
        {
            TriggerRefreshAsync(sut, refreshTrigger).GetAwaiter().GetResult();
            statusAfterRefresh = _viewModel.ProgressStatus;
            valueAfterRefresh = _viewModel.ProgressValue;
        });

        await sut.ProcessFormIdsAsync();

        Assert.Equal("Ingesting plugin 2 of 5: User.esp", statusAfterRefresh);
        // The second of five selected Plugins, which the run's progress renders as 40%.
        Assert.Equal(40, valueAfterRefresh);
        Assert.DoesNotContain(projectedStatuses, status => status.StartsWith("Scanning", StringComparison.Ordinal));
        // The run's own reports are the only thing the channel ever showed, and it ends empty.
        Assert.Equal(["Initializing...", "Ingesting plugin 2 of 5: User.esp", string.Empty], projectedStatuses);
    }

    /// <summary>
    ///     The user inputs that each start a Plugin List refresh, and stay live during a Processing Run because the
    ///     window disables nothing.
    /// </summary>
    public enum RefreshTrigger
    {
        /// <summary>Toggling Advanced Mode.</summary>
        AdvancedMode,

        /// <summary>Choosing a different GameRelease.</summary>
        GameRelease,

        /// <summary>Browsing to a game directory.</summary>
        GameDirectory
    }

    /// <summary>
    ///     Runs a handler once, the first time the progress channel shows the given run status.
    /// </summary>
    /// <param name="status">The run status text to wait for.</param>
    /// <param name="handler">The work to run while that run is still in flight.</param>
    /// <remarks>
    ///     A Processing Run reports its progress synchronously through the workflow, so the resulting notification is
    ///     raised from inside the run. That makes this the seam for acting mid-run without gating the executor. The
    ///     handler runs once because its own work changes the status again and would otherwise recurse.
    /// </remarks>
    private void OnRunStatus(string status, Action handler)
    {
        var handled = false;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (handled ||
                args.PropertyName != nameof(MainWindowViewModel.ProgressStatus) ||
                _viewModel.ProgressStatus != status)
            {
                return;
            }

            handled = true;
            handler();
        };
    }

    /// <summary>
    ///     Starts recording every status the transient progress channel shows from this point on.
    /// </summary>
    /// <returns>The list that receives the projected statuses, in the order they were shown.</returns>
    /// <remarks>
    ///     Attached after a test's setup rather than in the fixture, so a Plugin List refresh that ran while the run
    ///     was being configured does not land in a sequence that is supposed to be the run's alone.
    /// </remarks>
    private List<string> RecordProjectedStatuses()
    {
        var projectedStatuses = new List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.ProgressStatus))
            {
                projectedStatuses.Add(_viewModel.ProgressStatus);
            }
        };

        return projectedStatuses;
    }

    /// <summary>
    ///     Triggers a Plugin List refresh through the requested user input.
    /// </summary>
    /// <param name="sut">The workflow under test.</param>
    /// <param name="refreshTrigger">The input to exercise.</param>
    private static Task TriggerRefreshAsync(UserWorkflow sut, RefreshTrigger refreshTrigger)
    {
        return refreshTrigger switch
        {
            RefreshTrigger.AdvancedMode => sut.SetAdvancedModeAsync(AdvancedMode.On),
            RefreshTrigger.GameRelease => sut.SelectGameReleaseAsync(GameRelease.Fallout4),
            RefreshTrigger.GameDirectory => sut.BrowseGameDirectoryAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(refreshTrigger), refreshTrigger, "Unsupported trigger.")
        };
    }

    [Fact]
    public void Dispose_CalledTwice_CancelsAndDisposesProcessingRunExecutorOnce()
    {
        var sut = CreateSut();

        sut.Dispose();
        sut.Dispose();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.Equal(1, _processingRunExecutor.DisposeCallCount);
        Assert.Throws<ObjectDisposedException>(() => _pluginList.Invalidate());
    }

    /// <summary>
    /// Verifies that a throwing run cancellation still retires every owned collaborator, and that a later cleanup
    /// failure cannot replace the cancellation failure the caller observes.
    /// </summary>
    [Fact]
    public void Dispose_CancellationCallbackThrows_StillDisposesCollaboratorsAndKeepsPrimaryFailure()
    {
        var callbackFailure = new InvalidOperationException("cancellation callback failed");
        _processingRunExecutor.CancelFailure = new AggregateException(callbackFailure);
        _processingRunExecutor.DisposeFailure = new InvalidOperationException("executor cleanup failed");
        var sut = CreateSut();

        var thrown = Assert.Throws<AggregateException>(() => sut.Dispose());

        Assert.Same(callbackFailure, Assert.Single(thrown.InnerExceptions));
        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.Equal(1, _processingRunExecutor.DisposeCallCount);
        Assert.Throws<ObjectDisposedException>(() => _pluginList.Invalidate());
    }

    /// <summary>
    /// Verifies that a cleanup failure with no other failure in flight still propagates and does not stop the
    /// remaining collaborator from being retired.
    /// </summary>
    [Fact]
    public void Dispose_StandaloneCleanupFailure_PropagatesAndStillRetiresPluginList()
    {
        var disposalFailure = new InvalidOperationException("executor cleanup failed");
        _processingRunExecutor.DisposeFailure = disposalFailure;
        var sut = CreateSut();

        var thrown = Assert.Throws<InvalidOperationException>(() => sut.Dispose());

        Assert.Same(disposalFailure, thrown);
        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.Throws<ObjectDisposedException>(() => _pluginList.Invalidate());
    }

    /// <summary>
    /// Verifies the middle of the disposal order: the Game Context is retired before the executor is cancelled, so an
    /// installed-location lookup that finishes while cancellation runs cannot project a directory or refresh the
    /// Plugin List the workflow is about to dispose.
    /// </summary>
    /// <remarks>
    /// The lookup is released from inside the executor's cancellation, which is the first step after the Game Context
    /// slot. The step before it, detaching the Plugin List presentation adapter, is pinned against cancellation by
    /// <see cref="Dispose_QueuedPluginListProjection_DetachesPresentationBeforeCancellingExecutor" />. Retiring the Game
    /// Context has no effect at the moment it runs, only on later work, so its order relative to the adapter is
    /// not observable and is kept by the source order alone, which
    /// <c>CoreProjectBoundaryTests.UserWorkflowDispose_TeardownOrder_DetachesPresentationThenRetiresGameContextThenCancelsExecutor</c>
    /// pins.
    /// </remarks>
    [Fact]
    public async Task Dispose_LookupFinishingDuringExecutorCancellation_CannotPublishIntoTeardown()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Games\Fallout4");
        GateInstalledLocationLookup(GameRelease.Fallout4, lookupStarted, allowLookupToFinish);
        var sut = CreateSut();
        // Started from the thread pool, so the selection's continuations never need the test's own context, which is
        // blocked inside the cancellation callback below while it waits for them.
        var selection = Task.Run(
            () => sut.SelectGameReleaseAsync(GameRelease.Fallout4),
            TestContext.Current.CancellationToken);
        await lookupStarted.Task;
        string? directoryDuringCancel = null;
        var refreshesDuringCancel = -1;
        _processingRunExecutor.OnCancel = () =>
        {
            allowLookupToFinish.SetResult();
            selection.GetAwaiter().GetResult();
            directoryDuringCancel = _viewModel.GameDirectory;
            refreshesDuringCancel = _refreshes.Count;
        };

        sut.Dispose();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.Equal(string.Empty, directoryDuringCancel);
        Assert.Equal(0, refreshesDuringCancel);
        Assert.Empty(_viewModel.DetectedDirectories);
    }

    /// <summary>
    /// Verifies that disposal detaches Plugin List presentation before any other teardown step, so a projection already
    /// queued on the dispatcher cannot write to the ViewModel when cancelling the executor lets that dispatcher run.
    /// </summary>
    [Fact]
    public async Task Dispose_QueuedPluginListProjection_DetachesPresentationBeforeCancellingExecutor()
    {
        var dispatcher = new RecordingThreadDispatcher();
        var viewModel = new MainWindowViewModel(dispatcher);
        // Standing scan activity from before the workflow existed, so a projection surviving disposal would clear it.
        viewModel.ApplyScanActivityProjection(new ActivityProjection(true, "Unchanged", 42));
        dispatcher.Drain();
        var executor = new RecordingProcessingRunExecutor([]) { OnCancel = dispatcher.Drain };
        _pluginListGameLoadOrders.PluginNames = ["User.esp"];
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        var sut = new UserWorkflow(
            viewModel,
            dispatcher,
            _fileDialogService.Object,
            _gameInstallations,
            _pluginList,
            executor,
            _defaultDatabaseRoot);
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        // The Plugin List is confirmed, but its projection is still waiting on the dispatcher.
        Assert.NotNull(_pluginList.Current.Confirmed);
        Assert.NotEqual(0, dispatcher.PendingCount);
        Assert.Empty(viewModel.Plugins);
        var postCountBeforeDispose = dispatcher.PostCount;

        sut.Dispose();
        dispatcher.Drain();

        Assert.Equal(1, executor.CancelCallCount);
        Assert.Empty(viewModel.Plugins);
        Assert.True(viewModel.IsProgressVisible);
        Assert.Equal("Unchanged", viewModel.ProgressStatus);
        Assert.Equal(42, viewModel.ProgressValue);
        Assert.Empty(viewModel.InformationMessages);
        Assert.Equal(postCountBeforeDispose, dispatcher.PostCount);
    }

    /// <summary>
    /// Verifies that the production factory wires the whole Core graph without touching an installed game, the file
    /// system, or the platform picker, and that the workflow it returns tears that graph down cleanly.
    /// </summary>
    /// <remarks>
    /// This deliberately calls nothing that would read a load order or installation records: the production probe and
    /// Game Load Orders would then reach the real machine. A wiring mistake such as a null collaborator, a throwing
    /// constructor, or a disposal failure still surfaces here without a WinUI host.
    /// </remarks>
    [Fact]
    public void Create_ProductionComposition_ConstructsWithoutIoAndDisposesCleanly()
    {
        var dispatcher = new SynchronousThreadDispatcher();
        var viewModel = new MainWindowViewModel(dispatcher);
        // Strict, so any picker call during construction or disposal fails the test instead of returning a default.
        var fileDialogService = new Mock<IFileDialogService>(MockBehavior.Strict);

        var sut = UserWorkflow.Create(viewModel, dispatcher, fileDialogService.Object);

        Assert.NotNull(sut);
        Assert.Null(viewModel.SelectedGame);
        Assert.Empty(viewModel.GameDirectory);
        Assert.Empty(viewModel.DetectedDirectories);
        Assert.Empty(viewModel.Plugins);
        Assert.Empty(viewModel.ErrorMessages);
        Assert.Empty(viewModel.WarningMessages);

        var disposalFailure = Record.Exception(sut.Dispose);

        Assert.Null(disposalFailure);
        Assert.Empty(viewModel.ErrorMessages);
        fileDialogService.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that production composition resolves the local application data directory and hands it to the
    /// workflow as its default database root, so no other production code has to read that location on its own.
    /// </summary>
    [Fact]
    public void Create_ProductionComposition_PassesResolvedLocalApplicationDataRootThrough()
    {
        var dispatcher = new SynchronousThreadDispatcher();
        var viewModel = new MainWindowViewModel(dispatcher);

        using var sut = UserWorkflow.Create(viewModel, dispatcher, new Mock<IFileDialogService>().Object);

        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            sut.DefaultDatabaseRoot);
    }

    /// <summary>
    /// Verifies that bulk commands target complete confirmed membership rather than the filtered projection.
    /// </summary>
    [Fact]
    public async Task SelectAllAndNonePlugins_ActiveFilter_ApplyToCompleteConfirmedMembership()
    {
        var sut = CreateSut();
        await ConfirmPluginListAsync(sut, ["Visible.esp", "Hidden.esp", "AlsoHidden.esp"]);
        _viewModel.PluginFilter = "Visible";
        Assert.Equal("Visible.esp", Assert.Single(_viewModel.FilteredPlugins).Name);

        sut.SelectAllPlugins();

        var selected = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        Assert.Equal(["Visible.esp", "Hidden.esp", "AlsoHidden.esp"], selected.SelectedPluginNames);

        sut.SelectNoPlugins();

        Assert.Empty(Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed).SelectedPluginNames);
    }

    private UserWorkflow CreateSut()
    {
        return new UserWorkflow(
            _viewModel,
            _dispatcher,
            _fileDialogService.Object,
            _gameInstallations,
            _pluginList,
            _processingRunExecutor,
            _defaultDatabaseRoot);
    }

    /// <summary>
    /// Holds one GameRelease's installed-location lookup open, so a test can act while it is still in flight.
    /// </summary>
    /// <param name="release">The GameRelease whose lookup is gated; every other release answers immediately.</param>
    /// <param name="started">Signalled once the gated lookup has begun.</param>
    /// <param name="allowToFinish">Completed by the test to let the gated lookup return.</param>
    private void GateInstalledLocationLookup(
        GameRelease release,
        TaskCompletionSource started,
        TaskCompletionSource allowToFinish)
    {
        OnInstalledLocationLookup(release, () =>
        {
            started.SetResult();
            allowToFinish.Task.GetAwaiter().GetResult();
        });
    }

    /// <summary>
    /// Runs a handler when one GameRelease's installed-location lookup begins.
    /// </summary>
    /// <param name="release">The GameRelease whose lookup the handler responds to.</param>
    /// <param name="handler">The work to run inside that lookup, which may block or throw.</param>
    private void OnInstalledLocationLookup(GameRelease release, Action handler)
    {
        _gameInstallationProbe.BeforeInstalledDirectoriesLookup = lookedUpRelease =>
        {
            if (lookedUpRelease == release)
            {
                handler();
            }
        };
    }

    /// <summary>
    /// Loads deterministic membership through the real Plugin List and returns its confirmation.
    /// </summary>
    private async Task<ConfirmedPluginList> ConfirmPluginListAsync(
        UserWorkflow sut,
        IReadOnlyList<string> pluginNames)
    {
        _pluginListGameLoadOrders.PluginNames = pluginNames;
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        return Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
    }

    /// <summary>
    ///     Builds a selected-Plugin outcome whose report agrees with the outcomes it is given.
    /// </summary>
    /// <param name="outcomes">One outcome per selected Plugin, in selection order.</param>
    /// <returns>A completed selected-Plugin outcome carrying that report.</returns>
    /// <remarks>
    ///     The ingestion request is rebuilt here rather than taken from the run, because a report validates its
    ///     outcomes against the selection they came from and the recording executor never builds one of its own.
    /// </remarks>
    private static PluginRunOutcome CreatePluginRunOutcome(params PluginIngestionOutcome[] outcomes)
    {
        var request = new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
            "ingestion.db",
            outcomes.Select(static outcome => outcome.PluginName),
            UpdateMode.Append);

        return new PluginRunOutcome(new PluginIngestionReport(request, outcomes));
    }

    /// <summary>
    /// Establishes one selected confirmed Plugin through the supported workflow boundary.
    /// </summary>
    private async Task ConfigureValidPluginProcessingRunAsync(UserWorkflow sut)
    {
        _viewModel.DatabasePath = DatabasePath;
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
    }

    private sealed class RecordingProcessingRunExecutor(List<ProcessingRunRequest> processingRuns)
        : IProcessingRunExecutor
    {
        public int CancelCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        /// <summary>
        ///     The failure raised by <see cref="Cancel" />, standing in for a registered run cancellation callback
        ///     that throws out of the real executor's cancellation source.
        /// </summary>
        public Exception? CancelFailure { get; set; }

        /// <summary>
        ///     Work run inside <see cref="Cancel" /> before any configured failure, standing in for cancellation
        ///     callbacks that let the UI dispatcher run queued work while the workflow is being torn down.
        /// </summary>
        public Action? OnCancel { get; set; }

        /// <summary>
        ///     The failure raised by <see cref="Dispose" />, standing in for an executor whose own cleanup fails.
        /// </summary>
        public Exception? DisposeFailure { get; set; }

        /// <summary>
        ///     The failure raised by <see cref="ExecuteAsync" /> after any configured events are reported, standing in
        ///     for a run that ends in a terminal failure rather than an outcome.
        /// </summary>
        public Exception? ExecuteFailure { get; set; }

        /// <summary>
        ///     How the run ends when it does not fail.
        /// </summary>
        /// <remarks>
        ///     The default is an empty text import, which adds no messages. Plans now require a non-empty
        ///     authoritative selection, so an empty plan cannot stand in for an uneventful outcome.
        /// </remarks>
        public ProcessingRunOutcome Outcome { get; set; } =
            new FormIdTextRunOutcome(new FormIdTextFileImportResult(0, 0));

        public List<ProcessingRunProgress> ProgressToReport { get; } = [];

        public Task<ProcessingRunOutcome> ExecuteAsync(
            ProcessingRunRequest request,
            IProgress<ProcessingRunProgress>? progress = null)
        {
            processingRuns.Add(request);
            foreach (var runEvent in ProgressToReport)
            {
                progress?.Report(runEvent);
            }

            return ExecuteFailure is { } failure
                ? Task.FromException<ProcessingRunOutcome>(failure)
                : Task.FromResult(Outcome);
        }

        public void Cancel()
        {
            CancelCallCount++;
            OnCancel?.Invoke();
            if (CancelFailure is { } failure)
            {
                throw failure;
            }
        }

        public void Dispose()
        {
            DisposeCallCount++;
            if (DisposeFailure is { } failure)
            {
                throw failure;
            }
        }
    }

    /// <summary>
    ///     Holds posted work until a test drains it, reporting UI-thread access only while that work runs.
    /// </summary>
    private sealed class RecordingThreadDispatcher : IThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _postedActions = new();
        private bool _hasAccess;
        private int _postCount;

        public int PendingCount => _postedActions.Count;

        public int PostCount => Volatile.Read(ref _postCount);

        public void Post(Action action)
        {
            Interlocked.Increment(ref _postCount);
            _postedActions.Enqueue(action);
        }

        public bool CheckAccess()
        {
            return _hasAccess;
        }

        /// <summary>
        ///     Runs queued work, including anything that work posts, until the queue is empty.
        /// </summary>
        public void Drain()
        {
            while (_postedActions.TryDequeue(out var action))
            {
                _hasAccess = true;
                try
                {
                    action();
                }
                finally
                {
                    _hasAccess = false;
                }
            }
        }
    }

    private sealed class RecordingPluginListGameLoadOrders(List<PluginListSource> refreshes)
        : PluginListGameLoadOrdersStub
    {
        public Func<PluginListSource, CancellationToken, Task<AvailablePluginsDiscoveryResult>>? Handler { get; set; }

        public IReadOnlyList<string> PluginNames { get; set; } = [];

        /// <inheritdoc />
        public override Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = PluginListSource.Create(gameRelease, canonicalDataDirectory);
            refreshes.Add(source);
            if (Handler is not null)
            {
                return Handler(source, cancellationToken);
            }

            return Task.FromResult(Discovered(PluginNames));
        }
    }
}
