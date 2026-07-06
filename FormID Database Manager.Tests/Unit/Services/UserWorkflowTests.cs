#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private readonly RecordingPluginListManager _pluginListManager;
    private readonly RecordingProcessingRun _processingRun;
    private readonly List<ProcessingRunRequest> _processingRuns = [];
    private readonly List<(string Directory, GameRelease Game, bool Advanced)> _refreshes = [];
    private readonly MainWindowViewModel _viewModel;

    public UserWorkflowTests()
    {
        _viewModel = new MainWindowViewModel(_dispatcher);
        _gameDetectionService = FormID_Database_Manager.TestUtilities.Mocks.MockFactory.CreateGameDetectionServiceMock();
        _pluginListManager = new RecordingPluginListManager(
            _gameDetectionService.Object,
            _viewModel,
            _dispatcher,
            _refreshes);
        _processingRun = new RecordingProcessingRun(_processingRuns);
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
        Assert.Equal((GameDirectory, GameRelease.SkyrimSE, false), Assert.Single(_refreshes));
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

        Assert.Equal((GameDirectory, GameRelease.SkyrimSE, false), Assert.Single(_refreshes));
    }

    [Fact]
    public async Task ApplyGameContextTransitionAsync_SelectedDetectedDirectoryChanged_RefreshesPluginListForCurrentGameContext()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = @"D:\Games\Skyrim";

        var sut = CreateSut();

        await sut.ApplyGameContextTransitionAsync(GameContextTransition.SelectedDetectedDirectoryChanged());

        Assert.Equal((@"D:\Games\Skyrim", GameRelease.SkyrimSE, false), Assert.Single(_refreshes));
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
        Assert.Equal((@"C:\NewFallout", GameRelease.Fallout4, false), Assert.Single(_refreshes));
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
        Assert.Equal((GameDirectory, GameRelease.SkyrimSE, false), Assert.Single(_refreshes));
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
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = GameDirectory;
        _viewModel.Plugins.Add(new PluginListItem { Name = "User.esp", IsSelected = true });

        var sut = CreateSut();
        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(DefaultDatabasePathProvider.CreateDefaultDatabasePath(GameRelease.SkyrimSE), _viewModel.DatabasePath);
        Assert.Equal(_viewModel.DatabasePath, run.DatabasePath);
        Assert.Equal("SkyrimSE.db", Path.GetFileName(run.DatabasePath));
        Assert.Equal(["User.esp"], run.PluginNames);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_UpdateModeOn_CreatesReplaceModeRunRequest()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = GameDirectory;
        _viewModel.DatabasePath = DatabasePath;
        _viewModel.UpdateMode = true;
        _viewModel.Plugins.Add(new PluginListItem { Name = "User.esp", IsSelected = true });

        var sut = CreateSut();
        await sut.ProcessFormIdsAsync();

        var run = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_processingRuns));
        Assert.Equal(UpdateMode.ReplacePluginRecords, run.UpdateMode);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_StartsNewRun_ClearsStaleWarnings()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = GameDirectory;
        _viewModel.DatabasePath = DatabasePath;
        _viewModel.WarningMessages.Add("Stale warning");
        _viewModel.Plugins.Add(new PluginListItem { Name = "User.esp", IsSelected = true });

        var sut = CreateSut();
        await sut.ProcessFormIdsAsync();

        Assert.Empty(_viewModel.WarningMessages);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_ProcessingRunWarningEvent_AddsWarningMessage()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = GameDirectory;
        _viewModel.DatabasePath = DatabasePath;
        _viewModel.Plugins.Add(new PluginListItem { Name = "User.esp", IsSelected = true });
        _processingRun.EventsToReport.Add(ProcessingRunEvent.Warning("Skipped User.esp"));

        var sut = CreateSut();
        await sut.ProcessFormIdsAsync();

        Assert.Contains("Skipped User.esp", _viewModel.WarningMessages);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    [Fact]
    public async Task ProcessFormIdsAsync_AlreadyProcessing_CancelsCurrentRunAndSetsCancellingState()
    {
        _viewModel.IsProcessing = true;

        var sut = CreateSut();
        await sut.ProcessFormIdsAsync();

        Assert.True(_processingRun.Cancelled);
        Assert.True(_viewModel.IsProcessing);
        Assert.Equal("Cancelling...", _viewModel.ProgressStatus);
    }

    [Fact]
    public async Task ApplyGameContextTransitionAsync_AdvancedModeChanged_RefreshesPluginListForCurrentGameContext()
    {
        _viewModel.SelectedGame = GameRelease.SkyrimSE;
        _viewModel.GameDirectory = GameDirectory;
        _viewModel.AdvancedMode = true;

        var sut = CreateSut();

        await sut.ApplyGameContextTransitionAsync(GameContextTransition.AdvancedModeChanged());

        Assert.Equal((GameDirectory, GameRelease.SkyrimSE, true), Assert.Single(_refreshes));
    }

    private UserWorkflow CreateSut()
    {
        return new UserWorkflow(
            _viewModel,
            _fileDialogService.Object,
            _gameDetectionService.Object,
            _gameLocationService.Object,
            _pluginListManager,
            _processingRun);
    }

    private sealed class RecordingProcessingRun(List<ProcessingRunRequest> processingRuns) : ProcessingRun
    {
        public bool Cancelled { get; private set; }

        public List<ProcessingRunEvent> EventsToReport { get; } = [];

        public override Task ExecuteAsync(
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

        public override void Cancel()
        {
            Cancelled = true;
        }

        public override void Dispose()
        {
        }
    }

    private sealed class RecordingPluginListManager(
        GameDetectionService gameDetectionService,
        MainWindowViewModel viewModel,
        IThreadDispatcher dispatcher,
        List<(string Directory, GameRelease Game, bool Advanced)> refreshes)
        : PluginListManager(gameDetectionService, viewModel, dispatcher)
    {
        public override Task RefreshPluginList(
            string gameDirectory,
            GameRelease gameRelease,
            ObservableCollection<PluginListItem> plugins,
            bool showAdvanced)
        {
            refreshes.Add((gameDirectory, gameRelease, showAdvanced));
            return Task.CompletedTask;
        }
    }
}
