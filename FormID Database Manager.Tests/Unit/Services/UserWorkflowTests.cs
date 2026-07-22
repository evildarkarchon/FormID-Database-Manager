#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
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

    private readonly SynchronousThreadDispatcher _dispatcher = new();
    private readonly Mock<IFileDialogService> _fileDialogService = new();
    private readonly Mock<GameDetectionService> _gameDetectionService;
    private readonly Mock<IGameLocationService> _gameLocationService = new();
    private readonly PluginList _pluginList;
    private readonly RecordingPluginListDiscovery _pluginListDiscovery;
    private readonly PluginListPresentationAdapter _pluginListPresentationAdapter;
    private readonly RecordingProcessingRunExecutor _processingRunExecutor;
    private readonly List<ProcessingRunRequest> _processingRuns = [];
    private readonly List<PluginListSource> _refreshes = [];
    private readonly MainWindowViewModel _viewModel;

    public UserWorkflowTests()
    {
        _viewModel = new MainWindowViewModel(_dispatcher);
        _gameDetectionService = FormID_Database_Manager.TestUtilities.Mocks.MockFactory.CreateGameDetectionServiceMock();
        _pluginListDiscovery = new RecordingPluginListDiscovery(_refreshes);
        _pluginList = new PluginList(_gameDetectionService.Object, _pluginListDiscovery);
        _pluginListPresentationAdapter = new PluginListPresentationAdapter(_pluginList, _viewModel, _dispatcher);
        _processingRunExecutor = new RecordingProcessingRunExecutor(_processingRuns);
    }

    [Fact]
    public async Task ApplyGameContextTransitionAsync_SelectedGameReleaseChanged_ClearsStaleStateLoadsInstalledDirectoriesAndRefreshesPlugins()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = @"C:\Old";
        _viewModel.DetectedDirectories.Add(@"C:\Old");
        _viewModel.Plugins.Add(new PluginListItem { Name = "Old.esp" });
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns([GameDirectory, @"D:\Games\Skyrim"]);

        var sut = CreateSut();

        await sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedGameReleaseChanged());

        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal([GameDirectory, @"D:\Games\Skyrim"], _viewModel.DetectedDirectories.ToArray());
        Assert.Empty(_viewModel.Plugins);
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, false);
    }

    /// <summary>
    /// Verifies that the selection event raised while clearing an old directory does not invalidate the new lookup.
    /// </summary>
    [Fact]
    public async Task ApplyGameContextTransitionAsync_SelectedGameReleaseChangedWithExistingDirectory_IgnoresResetSelectionChange()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowLookupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _viewModel.SelectedGame = GameRelease.Fallout4;
        _viewModel.GameDirectory = @"C:\Old";
        _viewModel.DetectedDirectories.Add(@"C:\Old");
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns(() =>
            {
                lookupStarted.SetResult();
                allowLookupToFinish.Task.GetAwaiter().GetResult();
                return [@"C:\NewFallout"];
            });

        var sut = CreateSut();

        var gameSelection = sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedGameReleaseChanged());
        await lookupStarted.Task;

        await sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedDetectedDirectoryChanged());

        allowLookupToFinish.SetResult();
        await gameSelection;

        Assert.Equal(@"C:\NewFallout", _viewModel.GameDirectory);
        AssertSingleRefresh(@"C:\NewFallout", GameRelease.Fallout4, false);
    }

    [Fact]
    public async Task ApplyGameContextTransitionAsync_SelectedGameReleaseChangedWithoutInstalledFolders_RecordsInformationMessage()
    {
        _viewModel.SelectedGame = GameRelease.Fallout4;
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns([]);

        var sut = CreateSut();

        await sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedGameReleaseChanged());

        Assert.Contains(
            "No installed locations found for Fallout4. Use Browse to select a directory.",
            _viewModel.InformationMessages);
        Assert.Empty(_refreshes);
    }

    [Fact]
    public async Task ApplyGameContextTransitionAsync_SelectedDetectedDirectoryChangedAfterWorkflowDirectoryUpdate_IgnoresProgrammaticSelectionChange()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns([GameDirectory, @"D:\Games\Skyrim"]);

        var sut = CreateSut();

        await sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedGameReleaseChanged());
        await sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedDetectedDirectoryChanged());

        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, false);
    }

    [Fact]
    public async Task ApplyGameContextTransitionAsync_SelectedDetectedDirectoryChanged_RefreshesPluginListForCurrentGameContext()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = @"D:\Games\Skyrim";

        var sut = CreateSut();

        await sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedDetectedDirectoryChanged());

        AssertSingleRefresh(@"D:\Games\Skyrim", GameRelease.SkyrimSE, false);
    }

    [Fact]
    public async Task ApplyGameContextTransitionAsync_OverlappingGameReleaseChanges_AppliesLatestSelectionOnly()
    {
        var olderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowOlderToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.SkyrimSE))
            .Returns(() =>
            {
                olderStarted.SetResult();
                allowOlderToFinish.Task.GetAwaiter().GetResult();
                return [@"C:\OldSkyrim"];
            });
        _gameLocationService.Setup(x => x.GetGameFolders(GameRelease.Fallout4))
            .Returns([@"C:\NewFallout"]);

        var sut = CreateSut();

        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        var olderSelection = sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedGameReleaseChanged());
        await olderStarted.Task;

        _viewModel.SelectedGame = GameRelease.Fallout4;
        var newerSelection = sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedGameReleaseChanged());

        allowOlderToFinish.SetResult();
        await Task.WhenAll(olderSelection, newerSelection);

        Assert.Equal(@"C:\NewFallout", _viewModel.GameDirectory);
        AssertSingleRefresh(@"C:\NewFallout", GameRelease.Fallout4, false);
    }

    [Fact]
    public async Task BrowseGameDirectoryAsync_SelectedDirectoryDetectsGameAndRefreshesPlugins()
    {
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        _gameDetectionService.Setup(x => x.DetectGame(GameDirectory)).Returns(GameRelease.SkyrimSE);

        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, false);
    }

    [Fact]
    public async Task BrowseGameDirectoryAsync_SelectedDirectoryWithoutDetectableGame_RecordsWorkflowError()
    {
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Success(GameDirectory));
        _gameDetectionService.Setup(x => x.DetectGame(GameDirectory)).Returns((GameRelease?)null);

        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Null(_viewModel.SelectedGame);
        Assert.Contains(
            "Could not detect game from directory. Please select a game from the dropdown.",
            _viewModel.ErrorMessages);
        Assert.Empty(_refreshes);
    }

    [Fact]
    public async Task BrowseGameDirectoryAsync_PickerCancel_LeavesStateUnchangedAndAddsNoError()
    {
        _viewModel.GameDirectory = @"C:\Existing";
        _viewModel.SelectedGame = GameRelease.Fallout4;
        _fileDialogService.Setup(x => x.SelectGameDirectory())
            .ReturnsAsync(FileDialogResult.Cancelled());

        var sut = CreateSut();

        await sut.BrowseGameDirectoryAsync();

        Assert.Equal(@"C:\Existing", _viewModel.GameDirectory);
        Assert.Equal(GameRelease.Fallout4, _viewModel.SelectedGame);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_refreshes);
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

    [Fact]
    public async Task ProcessFormIdsAsync_NoSelectedGame_RecordsValidationMessage()
    {
        var sut = CreateSut();

        await sut.ProcessFormIdsAsync();

        Assert.Contains("Please select a game from the dropdown first.", _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
        Assert.False(_viewModel.IsProcessing);
        Assert.Equal("Process FormIDs", _viewModel.ProcessButtonText);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_PluginIngestionWithoutGameDirectory_RecordsValidationMessage()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;

        var sut = CreateSut();

        await sut.ProcessFormIdsAsync();

        Assert.Contains("Game directory must be specified when processing plugins", _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_PluginIngestionWithoutSelectedPlugins_RecordsValidationMessage()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = GameDirectory;

        var sut = CreateSut();
        await sut.ProcessFormIdsAsync();
        Assert.Contains("No plugins selected", _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
    }

    /// <summary>
    /// Verifies that an empty confirmed selection retains the existing user-facing validation message.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_ConfirmedListWithoutSelection_RecordsExistingValidationMessage()
    {
        var sut = CreateSut();
        await ConfirmPluginListAsync(sut, ["User.esp"]);

        await sut.ProcessFormIdsAsync();

        Assert.Contains("No plugins selected", _viewModel.ErrorMessages);
        Assert.Empty(_processingRuns);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_FormIdTextFile_SkipsPluginSelectionValidation()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.DatabasePath = DatabasePath;
        _viewModel.FormIdListPath = FormIdListPath;

        var sut = CreateSut();
        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(FormIdListPath, run.FormIdListPath);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_EmptyDatabasePath_CreatesDefaultPathForSelectedGame()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
        _viewModel.DatabasePath = string.Empty;

        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(DefaultDatabasePathProvider.CreateDefaultDatabasePath(GameRelease.SkyrimSE), _viewModel.DatabasePath);
        Assert.Equal(_viewModel.DatabasePath, run.DatabasePath);
        Assert.Equal("SkyrimSE.db", Path.GetFileName(run.DatabasePath));
        Assert.Equal(["User.esp"], run.PluginNames);
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
        Assert.Equal(confirmed.Source.DataDirectory, run.GameDirectory);
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

        var refreshCompletion = new TaskCompletionSource<PluginListDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListDiscovery.Handler = (_, _) => refreshCompletion.Task;
        var refresh = sut.ApplyGameContextTransitionAsync(GameContextTransition.AdvancedModeChanged());

        await sut.ProcessFormIdsAsync();
        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));

        refreshCompletion.SetResult(PluginListDiscoveryResult.Completed(
            ["Third.esp", "Second.esp", "First.esp", "New.esp"]));
        await refresh;
        var refreshed = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        sut.SetPluginSelection(refreshed.MembershipVersion, "Second.esp", true);

        Assert.Equal(confirmed.Source.GameRelease, run.GameRelease);
        Assert.Equal(confirmed.Source.DataDirectory, run.GameDirectory);
        Assert.Equal(["First.esp", "Third.esp"], run.PluginNames);
    }

    /// <summary>
    /// Verifies that a different-source transition invalidates old selection before a run request can be created.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_DuringSourceTransition_DoesNotMixOldSelectionWithNewGameContext()
    {
        var sut = CreateSut();
        var confirmed = await ConfirmPluginListAsync(sut, ["Old.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "Old.esp", true);

        var refreshCompletion = new TaskCompletionSource<PluginListDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pluginListDiscovery.Handler = (_, _) => refreshCompletion.Task;
        _viewModel.SelectedGame = GameRelease.Fallout4;
        _viewModel.GameDirectory = @"D:\Games\Fallout4";

        var transition = sut.ApplyGameContextTransitionAsync(
            GameContextTransition.SelectedDetectedDirectoryChanged());
        Assert.Null(_pluginList.Current.Confirmed);

        await sut.ProcessFormIdsAsync();

        Assert.Empty(_processingRuns);
        Assert.Contains("No plugins selected", _viewModel.ErrorMessages);

        refreshCompletion.SetResult(PluginListDiscoveryResult.Completed(["New.esp"]));
        await transition;
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

    [Fact]
    public async Task ProcessFormIdsAsync_StartsNewRun_ClearsStaleWarnings()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _viewModel.WarningMessages.Add("Stale warning");

        await sut.ProcessFormIdsAsync();

        Assert.Empty(_viewModel.WarningMessages);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_ProcessingRunWarningEvent_AddsWarningMessage()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.EventsToReport.Add(ProcessingRunEvent.Warning("Skipped User.esp"));

        await sut.ProcessFormIdsAsync();

        Assert.Contains("Skipped User.esp", _viewModel.WarningMessages);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_ProcessingRunErrorEvent_AddsErrorMessage()
    {
        var sut = CreateSut();
        await ConfigureValidPluginProcessingRunAsync(sut);
        _processingRunExecutor.EventsToReport.Add(ProcessingRunEvent.Error("Failed User.esp"));

        await sut.ProcessFormIdsAsync();

        Assert.Contains("Failed User.esp", _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_AlreadyProcessing_CancelsCurrentRunAndSetsCancellingState()
    {
        _viewModel.IsProcessing = true;

        var sut = CreateSut();
        await sut.ProcessFormIdsAsync();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.True(_viewModel.IsProcessing);
        Assert.Equal("Cancelling...", _viewModel.ProgressStatus);
    }

    [Fact]
    public void Dispose_CalledTwice_CancelsAndDisposesProcessingRunExecutorOnce()
    {
        var sut = CreateSut();

        _pluginListPresentationAdapter.Dispose();
        sut.Dispose();
        sut.Dispose();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.Equal(1, _processingRunExecutor.DisposeCallCount);
        Assert.Throws<ObjectDisposedException>(() => _pluginList.Invalidate());
    }

    [Fact]
    public async Task ApplyGameContextTransitionAsync_AdvancedModeChanged_RefreshesPluginListForCurrentGameContext()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = GameDirectory;
        _viewModel.AdvancedMode = true;

        var sut = CreateSut();

        await sut.ApplyGameContextTransitionAsync(GameContextTransition.AdvancedModeChanged());

        AssertSingleRefresh(GameDirectory, GameRelease.SkyrimSE, true);
    }

    /// <summary>
    /// Verifies that incomplete Game Context explicitly invalidates confirmation and its presentation projection.
    /// </summary>
    [Fact]
    public async Task ApplyGameContextTransitionAsync_IncompleteContext_InvalidatesConfirmedPluginList()
    {
        var sut = CreateSut();
        await ConfirmPluginListAsync(sut, ["Old.esp"]);
        Assert.Single(_viewModel.Plugins);

        _viewModel.GameDirectory = string.Empty;
        await sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedDetectedDirectoryChanged());

        Assert.Null(_pluginList.Current.Confirmed);
        Assert.IsType<PluginListNoSourceActivity>(_pluginList.Current.Activity);
        Assert.Empty(_viewModel.Plugins);
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
            _fileDialogService.Object,
            _gameDetectionService.Object,
            _gameLocationService.Object,
            _pluginList,
            _processingRunExecutor);
    }

    /// <summary>
    /// Asserts one canonical discovery source and the confirmed Advanced Mode produced by that refresh.
    /// </summary>
    private void AssertSingleRefresh(string gameDirectory, GameRelease gameRelease, bool advancedMode)
    {
        var source = Assert.Single(_refreshes);
        Assert.Equal(PluginListSource.Create(gameRelease, gameDirectory), source);
        var confirmed = Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
        Assert.Equal(advancedMode ? AdvancedMode.On : AdvancedMode.Off, confirmed.AdvancedMode);
    }

    /// <summary>
    /// Loads deterministic membership through the real Plugin List and returns its confirmation.
    /// </summary>
    private async Task<ConfirmedPluginList> ConfirmPluginListAsync(
        UserWorkflow sut,
        IReadOnlyList<string> pluginNames)
    {
        _pluginListDiscovery.PluginNames = pluginNames;
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = GameDirectory;

        await sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedDetectedDirectoryChanged());

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

    private sealed class RecordingProcessingRunExecutor(List<ProcessingRunRequest> processingRuns)
        : IProcessingRunExecutor
    {
        public int CancelCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public List<ProcessingRunEvent> EventsToReport { get; } = [];

        public Task ExecuteAsync(
            ProcessingRunRequest request,
            IProgress<ProcessingRunEvent>? progress = null)
        {
            processingRuns.Add(request);
            foreach (var runEvent in EventsToReport)
            {
                progress?.Report(runEvent);
            }

            return Task.CompletedTask;
        }

        public void Cancel()
        {
            CancelCallCount++;
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }
    }

    private sealed class RecordingPluginListDiscovery(List<PluginListSource> refreshes) : IPluginListDiscovery
    {
        public Func<PluginListSource, CancellationToken, Task<PluginListDiscoveryResult>>? Handler { get; set; }

        public IReadOnlyList<string> PluginNames { get; set; } = [];

        /// <inheritdoc />
        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            refreshes.Add(source);
            if (Handler is not null)
            {
                return Handler(source, cancellationToken);
            }

            return Task.FromResult(PluginListDiscoveryResult.Completed(PluginNames));
        }
    }
}
