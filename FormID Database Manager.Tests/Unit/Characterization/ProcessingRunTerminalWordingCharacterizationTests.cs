#nullable enable

using System;
using System.Collections.Generic;
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

namespace FormID_Database_Manager.Tests.Unit.Characterization;

/// <summary>
///     Pins how the User Workflow ends a Processing Run: the exact wording of every terminal message, which ViewModel
///     message list it lands in, and what the transient progress channel showed while it happened.
/// </summary>
/// <remarks>
///     <para>
///         Issue #63, under parent #61. The run's own reports are pinned by
///         <see cref="ProcessingRunWordingCharacterizationTests" /> and
///         <see cref="FormIdTextRunWordingCharacterizationTests" />; what is pinned here is the part the workflow owns
///         — its remaining two catch blocks, and the writes it performs from what
///         <see cref="ProcessingRunPresentation" /> renders out of the run's outcome.
///     </para>
///     <para>
///         Every test asserts all three message lists, not just the one it is about. Which list a terminal fact lands
///         in is the behaviour being pinned, so asserting only the populated list would let a message that also
///         appeared elsewhere pass. The projected progress statuses are asserted as a complete ordered sequence for
///         the same reason: the channel is transient and hands itself back at the end of a run, so the only way to
///         show a terminal fact never reached it is to state everything that did (issue #60).
///     </para>
///     <para>
///         The workflow fixture below duplicates <c>Unit.Services.UserWorkflowTests</c>, for the same reason the
///         sibling suite duplicates the executor's test doubles: parent #61 rewrites those suites, and a safety net
///         that borrowed their scaffolding would move along with the code it is meant to hold still.
///     </para>
/// </remarks>
public sealed class ProcessingRunTerminalWordingCharacterizationTests
{
    private const string GameDirectory = @"C:\Games\Skyrim";
    private const string DatabasePath = @"C:\Databases\formids.db";

    private readonly SynchronousThreadDispatcher _dispatcher = new();
    private readonly Mock<IFileDialogService> _fileDialogService = new();
    private readonly GameInstallations _gameInstallations;
    private readonly InMemoryGameInstallationProbe _gameInstallationProbe = new();
    private readonly PluginList _pluginList;
    private readonly StubPluginListDiscovery _pluginListDiscovery = new();
    private readonly List<string> _projectedStatuses = [];
    private readonly StubProcessingRunExecutor _processingRunExecutor = new();
    private readonly MainWindowViewModel _viewModel;

    /// <summary>
    ///     Builds the workflow's real collaborators around a recording Processing Run executor, and starts recording
    ///     every status the progress channel shows.
    /// </summary>
    /// <remarks>
    ///     No Plugin List presentation adapter is created: this suite asserts complete message-list contents, and the
    ///     adapter would add Plugin List information messages of its own that have nothing to do with how a run ended.
    /// </remarks>
    public ProcessingRunTerminalWordingCharacterizationTests()
    {
        _viewModel = new MainWindowViewModel(_dispatcher);
        _gameInstallations = new GameInstallations(_gameInstallationProbe);
        _pluginList = new PluginList(_pluginListDiscovery);
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.ProgressStatus))
            {
                _projectedStatuses.Add(_viewModel.ProgressStatus);
            }
        };
    }

    /// <summary>
    ///     Pins the acknowledgement a cancelled run leaves behind, the list it lands in, and the "Cancelling..." status
    ///     that the second button press puts on the channel instead of it.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_CancelledMidRun_PutsTheAcknowledgementInTheInformationMessagesOnly()
    {
        var sut = CreateSut();
        await ConfigureValidPluginRunAsync(sut);
        _processingRunExecutor.EventsToReport.Add(ProcessingRunEvent.Status("Processing User.esp", 40));
        // The second press is delivered from the run's own progress notification, so it genuinely lands mid-run. The
        // run then ends as cancelled because the press reached the executor, not because the test said so: the stub
        // returns the cancelled outcome it was asked for, exactly as the real executor does.
        OnRunStatus("Processing User.esp", () => sut.ProcessFormIdsAsync().GetAwaiter().GetResult());

        await sut.ProcessFormIdsAsync();

        Assert.Equal(1, _processingRunExecutor.CancelCallCount);
        Assert.Equal(["Processing cancelled by user."], _viewModel.InformationMessages);
        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Equal(
            ["Initializing...", "Processing User.esp", "Cancelling...", string.Empty],
            _projectedStatuses);
    }

    /// <summary>
    ///     Pins the validation message raised while the workflow builds the run request, shown unwrapped in the error
    ///     messages, with the run never reaching the executor.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_NoSelectedPlugins_ShowsTheValidationMessageUnwrapped()
    {
        var sut = CreateSut();
        _viewModel.DatabasePath = DatabasePath;
        await ConfirmPluginListAsync(sut, ["User.esp"]);

        await sut.ProcessFormIdsAsync();

        Assert.Equal(["No plugins selected"], _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.InformationMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Empty(_processingRunExecutor.Requests);
        AssertProgressChannelShowedOnlyTheRunStartAndCleanup();
    }

    /// <summary>
    ///     Pins the validation message for the other blank fact a Plugin run can be started with — a GameRelease that
    ///     resolved to no directory — which the same handler shows unwrapped.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_NoGameDirectory_ShowsTheGameDirectoryValidationMessageUnwrapped()
    {
        var sut = CreateSut();
        _viewModel.DatabasePath = DatabasePath;
        // A GameRelease with no installed location leaves the Game Context without a directory, which is the one way
        // this validation is reachable through the workflow.
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        // Resolving that release left its own information message, which says nothing about how a run ended.
        _viewModel.InformationMessages.Clear();
        _projectedStatuses.Clear();

        await sut.ProcessFormIdsAsync();

        Assert.Equal(["Game directory must be specified when processing plugins"], _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.InformationMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Empty(_processingRunExecutor.Requests);
        AssertProgressChannelShowedOnlyTheRunStartAndCleanup();
    }

    /// <summary>
    ///     Pins the message the workflow shows for its own gate, before any Processing Run request exists.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_NoSelectedGame_ShowsTheGameSelectionMessage()
    {
        var sut = CreateSut();

        await sut.ProcessFormIdsAsync();

        Assert.Equal(["Please select a game from the dropdown first."], _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.InformationMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Empty(_processingRunExecutor.Requests);
        AssertProgressChannelShowedOnlyTheRunStartAndCleanup();
    }

    /// <summary>
    ///     Pins the unresolvable-master message that names the master, shown unwrapped rather than behind the generic
    ///     processing-error prefix (ADR-0006, issue #52).
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_UnresolvableMasterNamingTheMaster_ShowsTheFailureMessageUnwrapped()
    {
        var sut = CreateSut();
        await ConfigureValidPluginRunAsync(sut);
        var failure = new UnresolvableMasterException("User.esp", "Starfield.esm");
        // The real run reports this status for any terminal failure before rethrowing, so it is reproduced here:
        // what matters is that it stays transient rather than becoming a second copy of the message.
        _processingRunExecutor.EventsToReport.Add(
            ProcessingRunEvent.Status($"Error during processing: {failure.Message}"));
        _processingRunExecutor.ExecuteFailure = failure;

        await sut.ProcessFormIdsAsync();

        Assert.Equal(
            [
                "Could not resolve 'Starfield.esm', a master file declared by User.esp. This game separates master " +
                "files by type in the load order, so that master must be present in the Data directory being " +
                "processed before any selected plugin can be read."
            ],
            _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.InformationMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Equal(
            [
                "Initializing...",
                $"Error during processing: {failure.Message}",
                string.Empty
            ],
            _projectedStatuses);
    }

    /// <summary>
    ///     Pins the unresolvable-master message for the case where the underlying failure named no individual master,
    ///     which is worded differently and is also shown unwrapped.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_UnresolvableMasterWithoutANamedMaster_ShowsTheFailureMessageUnwrapped()
    {
        var sut = CreateSut();
        await ConfigureValidPluginRunAsync(sut);
        _processingRunExecutor.ExecuteFailure = new UnresolvableMasterException("User.esp", null);

        await sut.ProcessFormIdsAsync();

        Assert.Equal(
            [
                "Could not resolve the master files declared by User.esp. This game separates master files by type " +
                "in the load order, but no master file was found in the Data directory being processed."
            ],
            _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.InformationMessages);
        Assert.Empty(_viewModel.WarningMessages);
        AssertProgressChannelShowedOnlyTheRunStartAndCleanup();
    }

    /// <summary>
    ///     Pins the generic prefix every other terminal failure is wrapped in, and the list it lands in.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_UnexpectedRunFailure_WrapsTheMessageWithTheGenericProcessingErrorPrefix()
    {
        var sut = CreateSut();
        await ConfigureValidPluginRunAsync(sut);
        _processingRunExecutor.ExecuteFailure = new InvalidOperationException("store unavailable");

        await sut.ProcessFormIdsAsync();

        Assert.Equal(["Error processing FormIDs: store unavailable"], _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.InformationMessages);
        Assert.Empty(_viewModel.WarningMessages);
        AssertProgressChannelShowedOnlyTheRunStartAndCleanup();
    }

    /// <summary>
    ///     Pins where each part of a rendered run report lands: the warning message in the warning list, the failure
    ///     message in the error list, and the completion status only on the transient progress channel.
    /// </summary>
    [Fact]
    public async Task ProcessFormIdsAsync_RunOutcomeWithWarningsAndFailures_RoutesEachRenderedPartToItsOwnList()
    {
        var sut = CreateSut();
        await ConfigureValidPluginRunAsync(sut);
        _processingRunExecutor.Outcome = CreatePluginRunOutcome(
            new IngestedPlugin("User.esp", 1, new ProcessingWarning(1, ["Recoverable issue"])),
            new FailedPlugin(
                "Bad.esp",
                new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header.")));

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
        Assert.Empty(_viewModel.InformationMessages);
        Assert.Equal(
            [
                "Initializing...",
                "Processing completed with failures: 1 ingested, 0 skipped, and 1 failed Plugins.",
                string.Empty
            ],
            _projectedStatuses);
    }

    /// <summary>
    ///     Builds a selected-Plugin outcome whose report agrees with the outcomes it is given.
    /// </summary>
    /// <param name="outcomes">One outcome per selected Plugin, in selection order.</param>
    /// <returns>A completed selected-Plugin outcome carrying that report.</returns>
    /// <remarks>
    ///     The ingestion request is rebuilt here rather than taken from the run, because a report validates its
    ///     outcomes against the selection they came from and the stub never sees an ingestion request of its own.
    /// </remarks>
    private static PluginRunOutcome CreatePluginRunOutcome(params PluginIngestionOutcome[] outcomes)
    {
        var request = new SelectedPluginIngestionRequest(
            GameDirectory,
            GameRelease.SkyrimSE,
            outcomes.Select(static outcome => outcome.PluginName),
            UpdateMode.Append);

        return new PluginRunOutcome(new PluginIngestionReport(request, outcomes));
    }

    /// <summary>
    ///     Asserts that the progress channel showed the run's own start status and nothing else before the run's
    ///     cleanup handed the channel back.
    /// </summary>
    private void AssertProgressChannelShowedOnlyTheRunStartAndCleanup()
    {
        Assert.Equal(["Initializing...", string.Empty], _projectedStatuses);
    }

    /// <summary>
    ///     Runs a handler once, the first time the progress channel shows the given run status.
    /// </summary>
    /// <param name="status">The run status text to wait for.</param>
    /// <param name="handler">The work to run while that run is still in flight.</param>
    /// <remarks>
    ///     A Processing Run reports its progress synchronously through the workflow, so the resulting notification is
    ///     raised from inside the run. The handler runs once because its own work changes the status again and would
    ///     otherwise recurse.
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

    private UserWorkflow CreateSut()
    {
        return new UserWorkflow(
            _viewModel,
            _fileDialogService.Object,
            _gameInstallations,
            _pluginList,
            _processingRunExecutor);
    }

    /// <summary>
    ///     Loads deterministic membership through the real Plugin List and returns its confirmation.
    /// </summary>
    /// <param name="sut">The workflow under test.</param>
    /// <param name="pluginNames">The Plugin names discovery reports.</param>
    /// <returns>The confirmed Plugin List produced by the refresh.</returns>
    private async Task<ConfirmedPluginList> ConfirmPluginListAsync(UserWorkflow sut, IReadOnlyList<string> pluginNames)
    {
        _pluginListDiscovery.PluginNames = pluginNames;
        _gameInstallationProbe.WithInstalledDirectories(GameRelease.SkyrimSE, GameDirectory);

        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);

        return Assert.IsType<ConfirmedPluginList>(_pluginList.Current.Confirmed);
    }

    /// <summary>
    ///     Establishes one selected confirmed Plugin and a database path, so the next process request is valid.
    /// </summary>
    /// <param name="sut">The workflow under test.</param>
    private async Task ConfigureValidPluginRunAsync(UserWorkflow sut)
    {
        _viewModel.DatabasePath = DatabasePath;
        var confirmed = await ConfirmPluginListAsync(sut, ["User.esp"]);
        sut.SetPluginSelection(confirmed.MembershipVersion, "User.esp", true);
    }

    /// <summary>
    ///     A Processing Run executor that reports a fixed script of events and then optionally fails, standing in for
    ///     the run whose own wording is pinned elsewhere in this suite.
    /// </summary>
    private sealed class StubProcessingRunExecutor : IProcessingRunExecutor
    {
        private bool _cancellationRequested;

        /// <summary>
        ///     The requests handed to this executor, so a run that never reached it can be told from one that did.
        /// </summary>
        public List<ProcessingRunRequest> Requests { get; } = [];

        /// <summary>
        ///     The events reported to the caller, in order, before the run ends.
        /// </summary>
        public List<ProcessingRunEvent> EventsToReport { get; } = [];

        /// <summary>
        ///     The number of cancellation requests this executor received.
        /// </summary>
        public int CancelCallCount { get; private set; }

        /// <summary>
        ///     The failure raised after the scripted events, standing in for a run that ends in a terminal failure.
        /// </summary>
        public Exception? ExecuteFailure { get; set; }

        /// <summary>
        ///     How the run ends when it is not cancelled and does not fail.
        /// </summary>
        /// <remarks>
        ///     The default is a dry run of nothing, which renders no message and no status at all, so a test that is
        ///     not about how a run ended sees only the wording it scripted for itself.
        /// </remarks>
        public ProcessingRunOutcome Outcome { get; set; } = new PlannedRunOutcome(new PluginRunPlan([]));

        /// <inheritdoc />
        public Task<ProcessingRunOutcome> ExecuteAsync(
            ProcessingRunRequest request,
            IProgress<ProcessingRunEvent>? progress = null)
        {
            Requests.Add(request);
            foreach (var runEvent in EventsToReport)
            {
                progress?.Report(runEvent);
            }

            // Cancellation is checked after the scripted events because a mid-run press arrives from one of them, and
            // a run that was asked to cancel must end as cancelled rather than as whatever else was configured.
            if (_cancellationRequested)
            {
                return Task.FromResult<ProcessingRunOutcome>(new CancelledRunOutcome());
            }

            return ExecuteFailure is { } failure
                ? Task.FromException<ProcessingRunOutcome>(failure)
                : Task.FromResult(Outcome);
        }

        /// <inheritdoc />
        public void Cancel()
        {
            CancelCallCount++;
            _cancellationRequested = true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    /// <summary>
    ///     Plugin List discovery that completes immediately with a fixed membership, so a Processing Run can be set up
    ///     without any file system.
    /// </summary>
    private sealed class StubPluginListDiscovery : IPluginListDiscovery
    {
        public IReadOnlyList<string> PluginNames { get; set; } = [];

        /// <inheritdoc />
        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PluginListDiscoveryResult.Completed(PluginNames));
        }
    }
}
