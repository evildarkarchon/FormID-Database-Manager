#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
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

    /// <summary>
    /// Verifies the Game Context sink wiring: a notice the composed Game Context reports reaches the ViewModel through
    /// the sink adapter this workflow builds for it.
    /// </summary>
    /// <remarks>
    /// Issue #120, under parent #118 (ADR-0014). What the adapter writes for each report is pinned by
    /// <see cref="ViewModelGameContextSinkTests" />, so this checks only that the Game Context reports to it. The
    /// expected text and list come from Game Context Presentation.
    /// </remarks>
    [Fact]
    public async Task BrowseGameDirectoryAsync_PickerFailure_ReachesTheViewModelThroughTheGameContextSink()
    {
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Failure("picker unavailable"));
        var rendered = GameContextPresentation.Render(new GameDirectoryPickerFailed("picker unavailable"));
        Assert.Equal(MessageList.Error, rendered.List);
        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal([rendered.Text], _viewModel.ErrorMessages);
    }

    /// <summary>
    /// Verifies the Processing Run Control sink wiring: the refusal the composed Processing Run Control reports for a
    /// press reaches the ViewModel through the sink adapter this workflow builds for it.
    /// </summary>
    /// <remarks>
    /// Issue #120, under parent #118 (ADR-0014). What the adapter writes for each report is pinned by
    /// <see cref="ViewModelProcessingRunControlSinkTests" />, and why the press is refused by
    /// <see cref="ProcessingRunAdmissionTests" />. The expected text comes from Processing Run Presentation.
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_NoGameSelected_ReachesTheViewModelThroughTheRunSink()
    {
        _viewModel.DatabasePath = DatabasePath;
        var sut = CreateSut();

        await sut.ProcessFormIdsAsync();

        Assert.Equal([ProcessingRunPresentation.Render(RunRejectionReason.NoGameSelected)], _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
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
    /// Verifies that the press captures the Game Context's Plugin List Source: with a GameRelease selected but no
    /// source held, the press is refused for the missing directory, as Processing Run Presentation renders it.
    /// </summary>
    /// <remarks>
    /// Which directories the Game Context refuses to hold is covered by <see cref="GameContextTests" />, and the
    /// refusal reason by <see cref="ProcessingRunAdmissionTests" />. This pins only that the workflow hands admission
    /// the Game Context's source, so no directory problem can reach the user as "Error processing FormIDs".
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_GameContextWithoutSource_RefusesForTheMissingDirectory()
    {
        _viewModel.DatabasePath = DatabasePath;
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.Null(_pluginList.Current.Confirmed);

        await sut.ProcessFormIdsAsync();

        Assert.Equal([ProcessingRunPresentation.Render(RunRejectionReason.NoGameDirectory)], _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
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
        Assert.Equal(
            [ProcessingRunPresentation.Render(RunRejectionReason.PluginListLoading)],
            _viewModel.ErrorMessages);

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
        Assert.Contains(ProcessingRunPresentation.Render(RunRejectionReason.NoGameDirectory), _viewModel.ErrorMessages);
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
        Assert.Equal(
            [ProcessingRunPresentation.Render(RunRejectionReason.PluginListLoading)],
            _viewModel.ErrorMessages);

        refreshCompletion.SetResult(PluginListGameLoadOrdersStub.Discovered(["New.esp"]));
        await transition;
    }

    /// <summary>
    /// Verifies that startup notifications cannot change the accepted run, but edits remain available to the next run.
    /// </summary>
    /// <param name="notification">The startup notification that edits the run inputs.</param>
    [Theory]
    [InlineData("Starting")]
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

        if (notification == "Starting")
        {
            OnRunStatus(StartingStatus(), EditInputs);
        }
        else if (notification == "Errors")
        {
            ((INotifyCollectionChanged)_viewModel.ErrorMessages).CollectionChanged += (_, _) => EditInputs();
        }
        else
        {
            ((INotifyCollectionChanged)_viewModel.WarningMessages).CollectionChanged += (_, _) => EditInputs();
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
    /// Verifies that a release change from the starting-status notification cannot invalidate the selection already
    /// accepted for a run.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_StartingStatusChangesGameContext_UsesCapturedSourceAndSelection()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        OnRunStatus(StartingStatus(), () => sut.SelectGameReleaseAsync(null).GetAwaiter().GetResult());

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(GameRelease.SkyrimSE, run.GameRelease);
        Assert.Equal(PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory), run.Source);
        Assert.Equal(["User.esp"], run.PluginNames);
        Assert.Null(_viewModel.SelectedGame);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    /// <summary>
    /// Verifies that text-run input capture preserves both its path and Dry Run decision across an edit made from the
    /// starting-status notification.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_StartingStatusEditsTextDryRun_UsesCapturedPathWithoutDefaulting()
    {
        var sut = CreateSut();
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        _viewModel.FormIdListPath = FormIdListPath;
        _viewModel.DryRun = true;
        OnRunStatus(StartingStatus(), () =>
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
        var ingestingProgress = new IngestingPlugin("User.esp", 2, 5);
        var ingesting = ProcessingRunPresentation.Render(ingestingProgress);
        Assert.NotNull(ingesting.Value);
        _processingRunExecutor.ProgressToReport.Add(ingestingProgress);
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
        OnRunStatus(ingesting.Status, () =>
        {
            TriggerRefreshAsync(sut, refreshTrigger).GetAwaiter().GetResult();
            statusAfterRefresh = _viewModel.ProgressStatus;
            valueAfterRefresh = _viewModel.ProgressValue;
        });

        await sut.ProcessFormIdsAsync();

        Assert.Equal(ingesting.Status, statusAfterRefresh);
        Assert.Equal(ingesting.Value.Value, valueAfterRefresh);
        Assert.DoesNotContain(projectedStatuses, status => status.StartsWith("Scanning", StringComparison.Ordinal));
        // The run's own reports are the only thing the channel ever showed, and it ends empty.
        Assert.Equal([StartingStatus(), ingesting.Status, string.Empty], projectedStatuses);
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
    ///     The starting status Processing Run Presentation renders for an admitted run, which this file compares
    ///     against rather than restating, so a wording change edits only <see cref="ProcessingRunPresentationTests" />.
    /// </summary>
    private static string StartingStatus()
    {
        return ProcessingRunPresentation.Render(new ProcessingRunStarting()).Status;
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

    /// <summary>
    /// Verifies that closing the window during a run cancels it without waiting for it to end: disposal returns while
    /// the run is still held in flight, the token the run was handed is cancelled, and a repeated disposal is a no-op.
    /// </summary>
    /// <remarks>
    /// Issue #104, under parent #102. The run's cancellation belongs to the Processing Run Control this workflow
    /// composes and retires (#105), so it is the token the run observes, not a call on the executor, that shows the run
    /// was cancelled. The run is released afterwards and ends as cancelled, as the real executor would end it.
    /// </remarks>
    [Fact]
    public async Task Dispose_DuringARun_CancelsTheRunWithoutWaitingForIt()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _processingRunExecutor.Gate = gate.Task;
        var run = sut.ProcessFormIdsAsync();
        var token = Assert.Single(_processingRunExecutor.ReceivedTokens);
        Assert.False(token.IsCancellationRequested);

        sut.Dispose();
        sut.Dispose();

        Assert.False(run.IsCompleted);
        Assert.True(token.IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(() => _pluginList.Invalidate());

        gate.SetResult();
        await run;
    }

    /// <summary>
    /// Verifies that a throwing run cancellation callback still lets disposal retire the Plugin List, and that the
    /// callback's failure propagates as the one failure the caller observes.
    /// </summary>
    /// <remarks>
    /// A cancellation callback registered on the run's token throws out of <see cref="CancellationTokenSource.Cancel()" />
    /// as an <see cref="AggregateException" />. Cancelling the run is the only teardown step a test can make fail, so
    /// this also covers a standalone cleanup failure: nothing else is in flight to take precedence over it.
    /// </remarks>
    [Fact]
    public async Task Dispose_RunCancellationCallbackThrows_PropagatesAndStillRetiresPluginList()
    {
        var callbackFailure = new InvalidOperationException("cancellation callback failed");
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _processingRunExecutor.Gate = gate.Task;
        _processingRunExecutor.OnExecute = token => token.Register(() => throw callbackFailure);
        var run = sut.ProcessFormIdsAsync();

        var thrown = Assert.Throws<AggregateException>(() => sut.Dispose());

        Assert.Same(callbackFailure, Assert.Single(thrown.InnerExceptions));
        Assert.True(Assert.Single(_processingRunExecutor.ReceivedTokens).IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(() => _pluginList.Invalidate());

        gate.SetResult();
        await run;
    }

    /// <summary>
    /// Verifies the middle of the disposal order: the Game Context is retired before the active run is cancelled, so
    /// an installed-location lookup that finishes while cancellation runs cannot project a directory or refresh the
    /// Plugin List the workflow is about to dispose.
    /// </summary>
    /// <remarks>
    /// The lookup is released from a callback registered on the run's token, so it finishes inside the run's
    /// cancellation, which is the step after the Game Context's. The step before it, detaching the Plugin List
    /// presentation adapter, is pinned against cancellation by
    /// <see cref="Dispose_QueuedPluginListProjection_DetachesPresentationBeforeCancellingTheRun" />. Retiring the Game
    /// Context has no effect at the moment it runs, only on later work, so its order relative to the adapter is
    /// not observable and is kept by the source order alone, which
    /// <c>CoreProjectBoundaryTests.UserWorkflowDispose_TeardownOrder_RetiresPresentationGameContextProcessingRunControlThenPluginList</c>
    /// pins.
    /// </remarks>
    [Fact]
    public async Task Dispose_LookupFinishingDuringRunCancellation_CannotPublishIntoTeardown()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _processingRunExecutor.Gate = gate.Task;
        var run = sut.ProcessFormIdsAsync();
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.Fallout4, @"C:\Games\Fallout4");
        GateInstalledLocationLookup(GameRelease.Fallout4, lookupStarted, allowLookupToFinish);
        // Started from the thread pool, so the selection's continuations never need the test's own context, which is
        // blocked inside the cancellation callback below while it waits for them.
        var selection = Task.Run(
            () => sut.SelectGameReleaseAsync(GameRelease.Fallout4),
            TestContext.Current.CancellationToken);
        await lookupStarted.Task;
        var refreshesBeforeDispose = _refreshes.Count;
        string? directoryDuringCancel = null;
        var refreshesDuringCancel = -1;
        Assert.Single(_processingRunExecutor.ReceivedTokens).Register(() =>
        {
            allowLookupToFinish.SetResult();
            selection.GetAwaiter().GetResult();
            directoryDuringCancel = _viewModel.GameDirectory;
            refreshesDuringCancel = _refreshes.Count;
        });

        sut.Dispose();

        Assert.True(Assert.Single(_processingRunExecutor.ReceivedTokens).IsCancellationRequested);
        // Selecting Fallout4 cleared the directory; the retired lookup never projected the one it found.
        Assert.Equal(string.Empty, directoryDuringCancel);
        Assert.Equal(refreshesBeforeDispose, refreshesDuringCancel);
        Assert.Empty(_viewModel.DetectedDirectories);

        gate.SetResult();
        await run;
    }

    /// <summary>
    /// Verifies that disposal detaches Plugin List presentation before any other teardown step, so a projection already
    /// queued on the dispatcher cannot write to the ViewModel when cancelling the active run lets that dispatcher run.
    /// </summary>
    /// <remarks>
    /// The dispatcher is drained from a callback registered on the run's token, so it runs inside the run's
    /// cancellation. That drain also applies the run's own queued starting activity, which is why the standing scan
    /// activity is checked only once the run has ended and handed the progress channel back: a Plugin List projection
    /// that survived disposal would have replaced it.
    /// </remarks>
    [Fact]
    public async Task Dispose_QueuedPluginListProjection_DetachesPresentationBeforeCancellingTheRun()
    {
        var dispatcher = new RecordingThreadDispatcher();
        var viewModel = new MainWindowViewModel(dispatcher);
        // Standing scan activity from before the workflow existed, so a projection surviving disposal would clear it.
        viewModel.ApplyScanActivityProjection(new ActivityProjection(true, "Unchanged", 42));
        dispatcher.Drain();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new RecordingProcessingRunExecutor([])
        {
            Gate = gate.Task,
            OnExecute = token => token.Register(dispatcher.Drain)
        };
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
        // A FormID text run, which needs only the GameRelease, and an explicit path so no default is written back.
        viewModel.FormIdListPath = FormIdListPath;
        viewModel.DatabasePath = DatabasePath;
        var run = sut.ProcessFormIdsAsync();
        var postCountBeforeDispose = dispatcher.PostCount;

        sut.Dispose();

        Assert.True(Assert.Single(executor.ReceivedTokens).IsCancellationRequested);
        Assert.Empty(viewModel.Plugins);
        Assert.Empty(viewModel.InformationMessages);
        Assert.Equal(postCountBeforeDispose, dispatcher.PostCount);

        gate.SetResult();
        await run;
        dispatcher.Drain();

        Assert.Empty(viewModel.Plugins);
        Assert.True(viewModel.IsProgressVisible);
        Assert.Equal("Unchanged", viewModel.ProgressStatus);
        Assert.Equal(42, viewModel.ProgressValue);
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

    /// <summary>
    /// Verifies that a whole-list press made while a newer confirmed membership's projection is still queued acts on
    /// the membership on screen, so the Plugin List refuses it as stale. Once the newer membership is drawn, the same
    /// press is applied.
    /// </summary>
    /// <param name="selectAll">Whether the press is Select All rather than Select None.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectAllAndNonePlugins_NewerMembershipNotYetProjected_RefusesUntilProjected(bool selectAll)
    {
        var dispatcher = new RecordingThreadDispatcher();
        var viewModel = new MainWindowViewModel(dispatcher);
        using var sut = CreateSut(viewModel, dispatcher);
        _pluginListGameLoadOrders.PluginNames = ["First.esp"];
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        dispatcher.Drain();
        var displayed = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        sut.SetPluginSelection(displayed.MembershipVersion, "First.esp", true);
        dispatcher.Drain();
        _pluginListGameLoadOrders.PluginNames = ["First.esp", "Second.esp"];
        await sut.SetAdvancedModeAsync(AdvancedMode.On);
        // The newer membership is confirmed, but the screen still shows the one the user is clicking against.
        var refreshed = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        Assert.NotEqual(displayed.MembershipVersion, refreshed.MembershipVersion);
        Assert.Equal(displayed.MembershipVersion, viewModel.ProjectedMembershipVersion);
        var stateRevisionBeforePress = _pluginList.Current.StateRevision;

        PressWholeListSelection(sut, selectAll);

        Assert.Equal(stateRevisionBeforePress, _pluginList.Current.StateRevision);
        Assert.Equal(
            ["First.esp"],
            Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed).SelectedPluginNames);

        dispatcher.Drain();
        PressWholeListSelection(sut, selectAll);

        Assert.Equal(
            selectAll ? ["First.esp", "Second.esp"] : [],
            Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed).SelectedPluginNames);
    }

    /// <summary>
    /// Verifies that a whole-list press does nothing while no Confirmed Plugin List is on screen, even when one has
    /// already been confirmed and is waiting to be drawn.
    /// </summary>
    /// <param name="selectAll">Whether the press is Select All rather than Select None.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectAllAndNonePlugins_NothingProjected_DoesNothing(bool selectAll)
    {
        var dispatcher = new RecordingThreadDispatcher();
        var viewModel = new MainWindowViewModel(dispatcher);
        using var sut = CreateSut(viewModel, dispatcher);
        _pluginListGameLoadOrders.PluginNames = ["First.esp"];
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        var confirmed = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        sut.SetPluginSelection(confirmed.MembershipVersion, "First.esp", !selectAll);
        Assert.Null(viewModel.ProjectedMembershipVersion);
        var stateRevisionBeforePress = _pluginList.Current.StateRevision;

        PressWholeListSelection(sut, selectAll);

        Assert.Equal(stateRevisionBeforePress, _pluginList.Current.StateRevision);
        Assert.Equal(
            selectAll ? [] : ["First.esp"],
            Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed).SelectedPluginNames);
    }

    /// <summary>
    /// Presses Select All or Select None through the workflow's public, parameterless shell operations.
    /// </summary>
    private static void PressWholeListSelection(UserWorkflow sut, bool selectAll)
    {
        if (selectAll)
        {
            sut.SelectAllPlugins();
        }
        else
        {
            sut.SelectNoPlugins();
        }
    }

    /// <summary>
    /// Creates a workflow over a caller-supplied ViewModel and dispatcher, with the fixture's real Plugin List and
    /// collaborators, so a test can hold the workflow's queued projections on a <see cref="RecordingThreadDispatcher" />.
    /// </summary>
    private UserWorkflow CreateSut(MainWindowViewModel viewModel, IThreadDispatcher dispatcher)
    {
        return new UserWorkflow(
            viewModel,
            dispatcher,
            _fileDialogService.Object,
            _gameInstallations,
            _pluginList,
            _processingRunExecutor,
            _defaultDatabaseRoot);
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
    /// Establishes one selected confirmed Plugin through the supported workflow boundary.
    /// </summary>
    private async Task ConfigureValidPluginProcessingRunAsync(UserWorkflow sut)
    {
        _viewModel.DatabasePath = DatabasePath;
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
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
