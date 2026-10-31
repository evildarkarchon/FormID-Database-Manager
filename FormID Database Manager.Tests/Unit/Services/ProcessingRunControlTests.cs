#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers Processing Run Control: how it decides each press of Process FormIDs — start the run admission admits,
///     report a refusal, or cancel the active run — and what it reports to its sink while that run is active (ADR-0012).
/// </summary>
/// <remarks>
///     <para>
///         Issue #105, under parent #102. The module is driven through its own interface: captured
///         <see cref="ProcessingRunInputs" /> records go in, and what comes out is the ordered record a
///         <see cref="RecordingProcessingRunControlSink" /> receives, together with the request and token the
///         <see cref="RecordingProcessingRunExecutor" /> was handed. No Game Context or ViewModel is involved.
///     </para>
///     <para>
///         Processing Run Admission and Processing Run Presentation run as real code. Every expected sink entry is
///         computed from Presentation's own output, so the wording itself is asserted only by
///         <see cref="ProcessingRunPresentationTests" />. Mid-run interactions happen from gates and callbacks inside
///         the run, never from timed waits.
///     </para>
/// </remarks>
public sealed class ProcessingRunControlTests : IDisposable
{
    private const string DatabasePath = @"C:\Databases\formids.db";
    private const string FormIdListPath = @"C:\Lists\formids.txt";
    private const string PluginName = "User.esp";

    /// <summary>
    ///     How a run ends, for the theory that covers every ending.
    /// </summary>
    /// <remarks>Public because a public theory method takes it as a parameter.</remarks>
    public enum RunEnding
    {
        /// <summary>The run completes a FormID text import.</summary>
        Completed,

        /// <summary>The run observes cancellation and returns the cancelled outcome.</summary>
        Cancelled,

        /// <summary>The run is a Dry Run and returns its plan.</summary>
        Planned,

        /// <summary>The run fails with an exception.</summary>
        Failed
    }

    /// <summary>
    ///     A default database root that is deliberately never created; admission only computes paths under it.
    /// </summary>
    private readonly string _defaultDatabaseRoot =
        Path.Combine(Path.GetTempPath(), $"FormIdTests-ControlRoot-{Guid.NewGuid():N}");

    private readonly RecordingProcessingRunExecutor _executor = new();

    /// <summary>
    ///     A game directory that is never created: building a Plugin List Source from it is pure path arithmetic.
    /// </summary>
    private readonly string _gameDirectory =
        Path.Combine(Path.GetTempPath(), $"FormIdTests-ControlGame-{Guid.NewGuid():N}");

    /// <summary>
    ///     Plugin Lists built to produce captured states, disposed with the test.
    /// </summary>
    private readonly List<PluginList> _pluginLists = [];

    private readonly RecordingProcessingRunControlSink _sink = new();

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var pluginList in _pluginLists)
        {
            pluginList.Dispose();
        }
    }

    /// <summary>
    ///     Verifies that every refusal reason reaches the sink as the list clear followed by Presentation's rendered
    ///     refusal, and nothing else: no starting activity is published and no run reaches the executor.
    /// </summary>
    /// <param name="reasonName">
    ///     The <see cref="RunRejectionReason" /> name, passed as a string because the enum is internal and a public test
    ///     method cannot take it as a parameter.
    /// </param>
    [Theory]
    [InlineData(nameof(RunRejectionReason.NoGameSelected))]
    [InlineData(nameof(RunRejectionReason.NoGameDirectory))]
    [InlineData(nameof(RunRejectionReason.PluginListLoading))]
    [InlineData(nameof(RunRejectionReason.PluginListUnavailable))]
    [InlineData(nameof(RunRejectionReason.NoPluginsSelected))]
    [InlineData(nameof(RunRejectionReason.NoDatabaseLocation))]
    public async Task PressAsync_RefusedInputs_ReportsTheListClearThenTheRenderedRefusal(string reasonName)
    {
        var reason = Enum.Parse<RunRejectionReason>(reasonName);
        var (inputs, defaultDatabaseRoot, held) = await RefusedInputsAsync(reason);
        // Built directly, because the root may be deliberately null here and CreateSut would substitute its own.
        using var sut = new ProcessingRunControl(_executor, defaultDatabaseRoot, _sink);

        await sut.PressAsync(inputs);

        Assert.Equal(
            [new MessageListsCleared(), new ErrorMessageAdded(ProcessingRunPresentation.Render(reason))],
            _sink.Entries);
        Assert.Empty(_executor.Requests);
        held?.Release();
    }

    /// <summary>
    ///     Verifies that a refused press with no database path chosen reports no defaulted path, even with a default
    ///     root to compute one under: only an admitted run gets a default.
    /// </summary>
    /// <remarks>
    ///     Issue #106, under parent #102. This moved here from the User Workflow tests, which used to pin it through the
    ///     database path box; the box is written only when this module reports a defaulted path.
    /// </remarks>
    [Fact]
    public async Task PressAsync_RefusedWithAnEmptyDatabasePath_ReportsNoDefaultedPath()
    {
        var pluginList = NewPluginList(ScriptedGameLoadOrders.Fixed(PluginName));
        var source = Source();
        await RefreshAsync(pluginList, source);
        var inputs = PluginInputs(pluginList.Current, source) with { DatabasePath = string.Empty };
        using var sut = CreateSut();

        await sut.PressAsync(inputs);

        Assert.Equal(
            [
                new MessageListsCleared(),
                new ErrorMessageAdded(ProcessingRunPresentation.Render(RunRejectionReason.NoPluginsSelected))
            ],
            _sink.Entries);
        Assert.Empty(_executor.Requests);
    }

    /// <summary>
    ///     Verifies that a refusal leaves the gate inactive: the next press starts a run rather than cancelling one.
    /// </summary>
    [Fact]
    public async Task PressAsync_AfterARefusal_StartsARunRatherThanCancelling()
    {
        using var sut = CreateSut();
        await sut.PressAsync(TextInputs(gameRelease: null));

        await sut.PressAsync(TextInputs());

        Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(_executor.Requests));
        Assert.False(Assert.Single(_executor.ReceivedTokens).IsCancellationRequested);
    }

    /// <summary>
    ///     Verifies an admitted selected-Plugin run: the executor is handed admission's request over the captured
    ///     selection with a live token, and the sink receives the list clear, the starting activity, the outcome's
    ///     completion line as information, and finally no activity.
    /// </summary>
    [Fact]
    public async Task PressAsync_AdmittedPluginRun_HandsTheExecutorTheRequestAndAnUncancelledToken()
    {
        var inputs = await PluginInputsAsync();
        using var sut = CreateSut();

        await sut.PressAsync(inputs);

        var request = Assert.IsType<PluginProcessingRunRequest>(Assert.Single(_executor.Requests));
        Assert.Equal([PluginName], request.PluginNames);
        Assert.Equal(DatabasePath, request.DatabasePath);
        Assert.False(request.DryRun);
        Assert.False(Assert.Single(_executor.ReceivedTokens).IsCancellationRequested);
        Assert.Equal(
            [
                new MessageListsCleared(),
                StartingActivity(),
                .. ProcessingRunPresentation.Render(_executor.Outcome).InformationMessages
                    .Select(static message => new InformationMessageAdded(message)),
                new RunActivityReported(ActivityProjection.None)
            ],
            _sink.Entries);
    }

    /// <summary>
    ///     Verifies an admitted FormID text run reaches the executor as a text request with the captured modes.
    /// </summary>
    [Fact]
    public async Task PressAsync_AdmittedFormIdTextRun_HandsTheExecutorATextRequest()
    {
        using var sut = CreateSut();

        await sut.PressAsync(TextInputs(updateMode: UpdateMode.ReplacePluginRecords));

        var request = Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(_executor.Requests));
        Assert.Equal(FormIdListPath, request.FormIdListPath);
        Assert.Equal(GameRelease.SkyrimSE, request.GameRelease);
        Assert.Equal(UpdateMode.ReplacePluginRecords, request.UpdateMode);
        Assert.False(Assert.Single(_executor.ReceivedTokens).IsCancellationRequested);
    }

    /// <summary>
    ///     Verifies an admitted Dry Run: the request is a Dry Run, the plan goes to the information messages, and
    ///     because the plan has nothing to show on the transient channel the activity goes straight back to none.
    /// </summary>
    [Fact]
    public async Task PressAsync_AdmittedDryRun_ReportsThePlanAsInformationAndNoPlanActivity()
    {
        _executor.Outcome = new PlannedRunOutcome(new FormIdTextRunPlan(FormIdListPath, 1234));
        using var sut = CreateSut();

        await sut.PressAsync(TextInputs(dryRun: true));

        Assert.True(Assert.IsType<FormIdTextProcessingRunRequest>(Assert.Single(_executor.Requests)).DryRun);
        var rendered = ProcessingRunPresentation.Render(_executor.Outcome);
        Assert.Equal(
            [
                new MessageListsCleared(),
                StartingActivity(),
                .. rendered.InformationMessages.Select(static message => new InformationMessageAdded(message)),
                new RunActivityReported(ActivityProjection.None)
            ],
            _sink.Entries);
    }

    /// <summary>
    ///     Verifies that a database path admission defaulted is reported after the starting activity, together with
    ///     the path that was captured, so the sink adapter can keep an edit made in the meantime.
    /// </summary>
    [Fact]
    public async Task PressAsync_AdmittedWithADefaultedDatabasePath_ReportsItWithTheCapturedPath()
    {
        var inputs = TextInputs(databasePath: string.Empty);
        var admitted = Assert.IsType<AdmittedRun>(ProcessingRunAdmission.Admit(inputs, _defaultDatabaseRoot));
        Assert.NotNull(admitted.DefaultedDatabasePath);
        using var sut = CreateSut();

        await sut.PressAsync(inputs);

        Assert.Equal(
            [
                new MessageListsCleared(),
                StartingActivity(),
                new DefaultedDatabasePathReported(admitted.DefaultedDatabasePath, string.Empty)
            ],
            _sink.Entries.Take(3));
        Assert.Equal(admitted.DefaultedDatabasePath, Assert.Single(_executor.Requests).DatabasePath);
        Assert.False(Directory.Exists(_defaultDatabaseRoot));
    }

    /// <summary>
    ///     Verifies that clearing the message lists is the first thing every press that does not cancel reports, for a
    ///     refusal and for an admitted run alike, so what the user reads afterwards belongs to that press only.
    /// </summary>
    [Fact]
    public async Task PressAsync_EveryPressThatDoesNotCancel_ClearsTheMessageListsFirst()
    {
        using var sut = CreateSut();

        await sut.PressAsync(TextInputs(gameRelease: null));
        var refusalEntries = _sink.Entries.ToArray();
        _sink.Entries.Clear();
        await sut.PressAsync(TextInputs(databasePath: string.Empty));

        Assert.IsType<MessageListsCleared>(refusalEntries[0]);
        Assert.IsType<MessageListsCleared>(_sink.Entries[0]);
        Assert.Single(_sink.Entries.OfType<MessageListsCleared>());
    }

    /// <summary>
    ///     Verifies that a press while a run is active cancels the token that run was handed and reports only the
    ///     cancellation-requested activity, carrying the run's percentage forward: the message lists are left alone,
    ///     and no second run is started.
    /// </summary>
    [Fact]
    public async Task PressAsync_WhileARunIsActive_CancelsItsTokenAndLeavesTheMessageListsAlone()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _executor.Gate = gate.Task;
        _executor.ProgressToReport.Add(new IngestingPlugin(PluginName, 2, 5));
        using var sut = CreateSut();
        var run = sut.PressAsync(TextInputs());
        var token = Assert.Single(_executor.ReceivedTokens);
        var ingesting = ProcessingRunPresentation.Render(new IngestingPlugin(PluginName, 2, 5));
        _sink.Entries.Clear();

        await sut.PressAsync(TextInputs());

        Assert.True(token.IsCancellationRequested);
        Assert.Single(_executor.Requests);
        Assert.Equal([CancellationRequestedActivity(ingesting.Value!.Value)], _sink.Entries);

        gate.SetResult();
        await run;
    }

    /// <summary>
    ///     Verifies that pressing again after cancellation was already requested reports the same cancellation-requested
    ///     activity and has no other effect: no list clear, no second run, and the run still ends as cancelled.
    /// </summary>
    [Fact]
    public async Task PressAsync_AfterCancellationWasRequested_OnlyReportsCancellationRequestedAgain()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _executor.Gate = gate.Task;
        using var sut = CreateSut();
        var run = sut.PressAsync(TextInputs());
        await sut.PressAsync(TextInputs());
        _sink.Entries.Clear();

        await sut.PressAsync(TextInputs());

        Assert.Equal([CancellationRequestedActivity(StartingValue())], _sink.Entries);
        Assert.Single(_executor.Requests);
        Assert.True(Assert.Single(_executor.ReceivedTokens).IsCancellationRequested);

        gate.SetResult();
        await run;

        Assert.Equal(
            [
                .. ProcessingRunPresentation.Render(new CancelledRunOutcome()).InformationMessages
                    .Select(static message => new InformationMessageAdded(message)),
                new RunActivityReported(ActivityProjection.None)
            ],
            _sink.Entries.Skip(1));
    }

    /// <summary>
    ///     Verifies the window this module exists to close: a press re-entering from any report an admitted press makes
    ///     before the run reaches the executor finds the run active, and the token the executor is then handed is
    ///     already cancelled, so the run ends with the normal cancellation acknowledgement.
    /// </summary>
    /// <param name="reentryPoint">The startup report whose sink callback presses again.</param>
    /// <remarks>
    ///     Issue #102. The token exists from the moment admission admits the press, before the first sink report, so
    ///     no startup notification can arrive at a run that has nothing to cancel yet.
    /// </remarks>
    [Theory]
    [InlineData(nameof(MessageListsCleared))]
    [InlineData(nameof(RunActivityReported))]
    [InlineData(nameof(DefaultedDatabasePathReported))]
    public async Task PressAsync_PressedFromAStartupReport_HandsTheExecutorAnAlreadyCancelledToken(
        string reentryPoint)
    {
        using var sut = CreateSut();
        var pressedAgain = false;
        _sink.OnEntry = entry =>
        {
            if (pressedAgain || entry.GetType().Name != reentryPoint)
            {
                return;
            }

            pressedAgain = true;
            sut.PressAsync(TextInputs(databasePath: string.Empty)).GetAwaiter().GetResult();
        };

        await sut.PressAsync(TextInputs(databasePath: string.Empty));

        Assert.True(pressedAgain);
        Assert.Single(_executor.Requests);
        Assert.True(Assert.Single(_executor.CancelledOnArrival));
        Assert.Single(_sink.Entries.OfType<MessageListsCleared>());
        Assert.Single(_sink.Entries.OfType<DefaultedDatabasePathReported>());
        Assert.Contains(CancellationRequestedActivity(StartingValue()), _sink.Entries);
        Assert.Equal(
            [
                .. ProcessingRunPresentation.Render(new CancelledRunOutcome()).InformationMessages
                    .Select(static message => new InformationMessageAdded(message)),
                new RunActivityReported(ActivityProjection.None)
            ],
            _sink.Entries.TakeLast(2));
    }

    /// <summary>
    ///     Verifies that a press re-entering from the list clear leaves its cancellation-requested activity as the last
    ///     thing the run shows before it ends: the starting activity is never published after it, which would put the
    ///     older status back on the channel.
    /// </summary>
    [Fact]
    public async Task PressAsync_PressedFromTheListClear_DoesNotPublishTheStartingActivityAfterTheCancellation()
    {
        using var sut = CreateSut();
        var pressedAgain = false;
        _sink.OnEntry = entry =>
        {
            if (pressedAgain || entry is not MessageListsCleared)
            {
                return;
            }

            pressedAgain = true;
            sut.PressAsync(TextInputs()).GetAwaiter().GetResult();
        };

        await sut.PressAsync(TextInputs());

        Assert.True(pressedAgain);
        Assert.Equal(
            [
                new MessageListsCleared(),
                CancellationRequestedActivity(StartingValue()),
                .. ProcessingRunPresentation.Render(new CancelledRunOutcome()).InformationMessages
                    .Select(static message => new InformationMessageAdded(message)),
                new RunActivityReported(ActivityProjection.None)
            ],
            _sink.Entries);
    }

    /// <summary>
    ///     Verifies the real executor over fake Plugin Ingestion and a fake Store opener: a press during the startup
    ///     window makes the run end as cancelled, and no FormID Record Store is ever opened.
    /// </summary>
    /// <remarks>
    ///     Issue #102, user story 3. The real executor offloads to a worker, so the run ends on a pool thread; the
    ///     press's task still completes only after the run's last report reached the sink.
    /// </remarks>
    [Fact]
    public async Task PressAsync_PressedDuringStartupWithTheRealExecutor_EndsCancelledWithoutOpeningAStore()
    {
        var inputs = await PluginInputsAsync();
        var ingestion = new UnexpectedPluginIngestion();
        var opener = new RecordingRecordStoreSessionOpener(new RecordingRecordStoreSession());
        using var sut = new ProcessingRunControl(
            new ProcessingRunExecutor(ingestion, new UnexpectedFormIdTextIngestion(), opener),
            _defaultDatabaseRoot,
            _sink);
        var pressedAgain = false;
        Task? reentrantPress = null;
        _sink.OnEntry = entry =>
        {
            if (pressedAgain || entry is not RunActivityReported { Activity.IsActive: true })
            {
                return;
            }

            // Set before pressing, because a press that wrongly started a second run would report its own starting
            // activity from inside this call. The press is not blocked on either: a second real run would otherwise
            // wait on a worker from inside this report, and the regression would show up as a hang rather than as
            // the failure below.
            pressedAgain = true;
            reentrantPress = sut.PressAsync(inputs);
        };

        await sut.PressAsync(inputs);

        Assert.NotNull(reentrantPress);
        await reentrantPress;
        Assert.Empty(opener.OpenCalls);
        Assert.Equal(0, ingestion.CallCount);
        Assert.Equal(
            [
                .. ProcessingRunPresentation.Render(new CancelledRunOutcome()).InformationMessages
                    .Select(static message => new InformationMessageAdded(message)),
                new RunActivityReported(ActivityProjection.None)
            ],
            _sink.Entries.TakeLast(2));
    }

    /// <summary>
    ///     Verifies that how a run ended reaches the sink as messages only, warnings then errors then the completion
    ///     line as information, before the activity returns to none with no terminal activity in between.
    /// </summary>
    /// <remarks>
    ///     Issue #94. The completion line used to be reported as the run's terminal activity, which the hand-back
    ///     erased in the same continuation, so a finished run left no trace of how it ended. The guard on the rendered
    ///     lists keeps this case from passing vacuously should the outcome stop rendering any of them.
    /// </remarks>
    [Fact]
    public async Task PressAsync_OutcomeWithWarningsAndFailures_ReportsWarningsErrorsAndInformationThenNoActivity()
    {
        _executor.Outcome = CreatePluginRunOutcome(
            new IngestedPlugin(PluginName, 1, new ProcessingWarning(1, ["Recoverable issue"])),
            new FailedPlugin("Bad.esp", new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid header.")));
        var rendered = ProcessingRunPresentation.Render(_executor.Outcome);
        Assert.NotEmpty(rendered.WarningMessages);
        Assert.NotEmpty(rendered.ErrorMessages);
        Assert.NotEmpty(rendered.InformationMessages);
        using var sut = CreateSut();

        await sut.PressAsync(TextInputs());

        Assert.Equal(
            [
                new MessageListsCleared(),
                StartingActivity(),
                .. rendered.WarningMessages.Select(static message => new WarningMessageAdded(message)),
                .. rendered.ErrorMessages.Select(static message => new ErrorMessageAdded(message)),
                .. rendered.InformationMessages.Select(static message => new InformationMessageAdded(message)),
                new RunActivityReported(ActivityProjection.None)
            ],
            _sink.Entries);
    }

    /// <summary>
    ///     Verifies that a progress report arriving after its run ended is dropped rather than put back on the channel.
    /// </summary>
    [Fact]
    public async Task PressAsync_ProgressReportedAfterTheRunEnded_IsDropped()
    {
        using var sut = CreateSut();
        await sut.PressAsync(TextInputs());
        var progress = Assert.Single(_executor.ReceivedProgress);
        Assert.NotNull(progress);
        var entriesAtEnd = _sink.Entries.Count;

        progress.Report(new IngestingPlugin(PluginName, 1, 2));

        Assert.Equal(entriesAtEnd, _sink.Entries.Count);
    }

    /// <summary>
    ///     Verifies that a progress report rendered with no new percentage keeps the percentage last shown.
    /// </summary>
    [Fact]
    public async Task PressAsync_ProgressWithoutANewPercentage_CarriesTheLastPercentageForward()
    {
        var ingesting = new IngestingPlugin(PluginName, 2, 5);
        var namedPlugin = new ImportingFormIdTextPlugin(PluginName);
        _executor.ProgressToReport.Add(ingesting);
        _executor.ProgressToReport.Add(namedPlugin);
        var renderedIngesting = ProcessingRunPresentation.Render(ingesting);
        var renderedNamedPlugin = ProcessingRunPresentation.Render(namedPlugin);
        Assert.Null(renderedNamedPlugin.Value);
        using var sut = CreateSut();

        await sut.PressAsync(TextInputs());

        Assert.Equal(
            [
                new RunActivityReported(new ActivityProjection(
                    true,
                    renderedIngesting.Status,
                    renderedIngesting.Value!.Value)),
                new RunActivityReported(new ActivityProjection(
                    true,
                    renderedNamedPlugin.Status,
                    renderedIngesting.Value!.Value))
            ],
            _sink.Entries.Skip(2).Take(2));
    }

    /// <summary>
    ///     Verifies that every failure, an Unresolvable Master or any other, takes one route to the errors, through
    ///     Presentation's failure renderer. How each kind reads is pinned by <see cref="ProcessingRunPresentationTests" />.
    /// </summary>
    /// <param name="unresolvableMaster">Whether the run fails with an Unresolvable Master.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PressAsync_RunFailure_ReportsTheRenderedFailureToTheErrors(bool unresolvableMaster)
    {
        Exception failure = unresolvableMaster
            ? new UnresolvableMasterException(PluginName, "Starfield.esm")
            : new InvalidOperationException("store unavailable");
        _executor.ExecuteFailure = failure;
        using var sut = CreateSut();

        await sut.PressAsync(TextInputs());

        var error = Assert.IsType<ErrorMessageAdded>(Assert.Single(_sink.Entries, static entry =>
            entry is ErrorMessageAdded));
        Assert.Equal(ProcessingRunPresentation.RenderFailure(failure), error.Message);
        Assert.Equal(
            [new MessageListsCleared(), StartingActivity(), error, new RunActivityReported(ActivityProjection.None)],
            _sink.Entries);
    }

    /// <summary>
    ///     Verifies that every way a run can end hands the progress channel back, and leaves the gate free so the next
    ///     press starts a new run rather than cancelling.
    /// </summary>
    /// <param name="ending">How the run ends.</param>
    [Theory]
    [InlineData(RunEnding.Completed)]
    [InlineData(RunEnding.Cancelled)]
    [InlineData(RunEnding.Planned)]
    [InlineData(RunEnding.Failed)]
    public async Task PressAsync_AnyRunEnding_ReturnsTheActivityToNone(RunEnding ending)
    {
        switch (ending)
        {
            case RunEnding.Completed:
                _executor.Outcome = new FormIdTextRunOutcome(new FormIdTextFileImportResult(0, 0));
                break;
            case RunEnding.Cancelled:
                _executor.Outcome = new CancelledRunOutcome();
                break;
            case RunEnding.Planned:
                _executor.Outcome = new PlannedRunOutcome(new FormIdTextRunPlan(FormIdListPath, null));
                break;
            case RunEnding.Failed:
                _executor.ExecuteFailure = new InvalidOperationException("store unavailable");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(ending), ending, "Unsupported run ending.");
        }

        using var sut = CreateSut();

        await sut.PressAsync(TextInputs());

        Assert.Equal(new RunActivityReported(ActivityProjection.None), _sink.Entries[^1]);

        _executor.ExecuteFailure = null;
        _sink.Entries.Clear();
        await sut.PressAsync(TextInputs());

        Assert.Equal(2, _executor.Requests.Count);
        Assert.False(_executor.ReceivedTokens[1].IsCancellationRequested);
        Assert.IsType<MessageListsCleared>(_sink.Entries[0]);
    }

    /// <summary>
    ///     Verifies that disposal cancels the run in flight and returns without waiting for it, and that a repeated
    ///     disposal is a no-op.
    /// </summary>
    [Fact]
    public async Task Dispose_DuringARun_CancelsItWithoutWaiting()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _executor.Gate = gate.Task;
        var sut = CreateSut();
        var run = sut.PressAsync(TextInputs());
        var token = Assert.Single(_executor.ReceivedTokens);
        Assert.False(token.IsCancellationRequested);

        sut.Dispose();
        sut.Dispose();

        Assert.False(run.IsCompleted);
        Assert.True(token.IsCancellationRequested);

        gate.SetResult();
        await run;

        Assert.Equal(new RunActivityReported(ActivityProjection.None), _sink.Entries[^1]);
    }

    /// <summary>
    ///     Verifies that a press after disposal is a programming error that throws, rather than a run failure rendered
    ///     to the errors.
    /// </summary>
    [Fact]
    public async Task PressAsync_AfterDispose_ThrowsObjectDisposedExceptionWithoutReporting()
    {
        var sut = CreateSut();
        sut.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.PressAsync(TextInputs()));

        Assert.Empty(_sink.Entries);
        Assert.Empty(_executor.Requests);
    }

    /// <summary>
    ///     Creates the module under test over the recording executor and sink and the uncreated default root.
    /// </summary>
    private ProcessingRunControl CreateSut()
    {
        return new ProcessingRunControl(_executor, _defaultDatabaseRoot, _sink);
    }

    /// <summary>
    ///     The starting activity Presentation renders, as the sink receives it when a run is admitted.
    /// </summary>
    private static RunActivityReported StartingActivity()
    {
        var starting = ProcessingRunPresentation.Render(new ProcessingRunStarting());
        return new RunActivityReported(new ActivityProjection(true, starting.Status, StartingValue()));
    }

    /// <summary>
    ///     The percentage the starting activity shows.
    /// </summary>
    private static double StartingValue()
    {
        return ProcessingRunPresentation.Render(new ProcessingRunStarting()).Value
               ?? throw new InvalidOperationException("The starting status is expected to carry a percentage.");
    }

    /// <summary>
    ///     The cancellation-requested activity Presentation renders, keeping the percentage already shown.
    /// </summary>
    /// <param name="shownValue">The percentage on screen when cancellation was requested.</param>
    private static RunActivityReported CancellationRequestedActivity(double shownValue)
    {
        var cancelling = ProcessingRunPresentation.Render(new ProcessingRunCancellationRequested());
        return new RunActivityReported(new ActivityProjection(true, cancelling.Status, cancelling.Value ?? shownValue));
    }

    /// <summary>
    ///     Builds captured inputs for a FormID text run, which needs no Plugin List, with a database path chosen.
    /// </summary>
    private ProcessingRunInputs TextInputs(
        GameRelease? gameRelease = GameRelease.SkyrimSE,
        string? databasePath = DatabasePath,
        UpdateMode updateMode = UpdateMode.Append,
        bool dryRun = false)
    {
        return new ProcessingRunInputs(
            gameRelease,
            null,
            NewPluginList(ScriptedGameLoadOrders.Fixed()).Current,
            FormIdListPath,
            databasePath,
            updateMode,
            dryRun);
    }

    /// <summary>
    ///     Builds captured inputs for a selected-Plugin run over a real Confirmed Plugin List selecting one Plugin.
    /// </summary>
    private async Task<ProcessingRunInputs> PluginInputsAsync()
    {
        var pluginList = NewPluginList(ScriptedGameLoadOrders.Fixed(PluginName));
        var source = Source();
        await RefreshAsync(pluginList, source);
        Select(pluginList);
        return PluginInputs(pluginList.Current, source);
    }

    /// <summary>
    ///     Builds captured inputs that admission refuses for exactly the given reason.
    /// </summary>
    /// <param name="reason">The refusal the inputs must produce.</param>
    /// <returns>
    ///     The inputs, the default database root to compose with, and a held discovery the test releases at the end
    ///     when the reason needs a Plugin List still loading.
    /// </returns>
    private async Task<(ProcessingRunInputs Inputs, string? DefaultDatabaseRoot, HeldDiscovery? Held)>
        RefusedInputsAsync(RunRejectionReason reason)
    {
        switch (reason)
        {
            case RunRejectionReason.NoGameSelected:
                return (TextInputs(gameRelease: null), _defaultDatabaseRoot, null);
            case RunRejectionReason.NoGameDirectory:
                return (PluginInputs(NewPluginList(ScriptedGameLoadOrders.Fixed()).Current, null),
                    _defaultDatabaseRoot, null);
            case RunRejectionReason.PluginListLoading:
            {
                var held = ScriptedDiscovery.Held(PluginName);
                var pluginList = NewPluginList(ScriptedGameLoadOrders.Sequenced(held));
                _ = RefreshAsync(pluginList, Source());
                return (PluginInputs(pluginList.Current, Source()), _defaultDatabaseRoot, held);
            }
            case RunRejectionReason.PluginListUnavailable:
                return (PluginInputs(NewPluginList(ScriptedGameLoadOrders.Fixed(PluginName)).Current, Source()),
                    _defaultDatabaseRoot, null);
            case RunRejectionReason.NoPluginsSelected:
            {
                var pluginList = NewPluginList(ScriptedGameLoadOrders.Fixed(PluginName));
                await RefreshAsync(pluginList, Source());
                return (PluginInputs(pluginList.Current, Source()), _defaultDatabaseRoot, null);
            }
            case RunRejectionReason.NoDatabaseLocation:
                return (TextInputs(databasePath: string.Empty), null, null);
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unsupported refusal reason.");
        }
    }

    /// <summary>
    ///     Builds captured selected-Plugin inputs from a Plugin List state and the Game Context's source.
    /// </summary>
    private static ProcessingRunInputs PluginInputs(PluginListState state, PluginListSource? source)
    {
        return new ProcessingRunInputs(
            GameRelease.SkyrimSE,
            source,
            state,
            null,
            DatabasePath,
            UpdateMode.Append,
            false);
    }

    /// <summary>
    ///     Builds the SkyrimSE Plugin List Source a Game Context would hold for the test's game directory.
    /// </summary>
    private PluginListSource Source()
    {
        return PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory);
    }

    /// <summary>
    ///     Builds a Plugin List the test disposes, so only it can construct the captured states (ADR-0009).
    /// </summary>
    private PluginList NewPluginList(ScriptedGameLoadOrders gameLoadOrders)
    {
        var pluginList = new PluginList(gameLoadOrders);
        _pluginLists.Add(pluginList);
        return pluginList;
    }

    /// <summary>
    ///     Refreshes a Plugin List for the given source.
    /// </summary>
    private static Task RefreshAsync(PluginList pluginList, PluginListSource source)
    {
        return pluginList.RefreshAsync(source, AdvancedMode.Off, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Selects the one test Plugin in the current Confirmed Plugin List.
    /// </summary>
    private static void Select(PluginList pluginList)
    {
        var confirmed = Assert.IsType<ConfirmedPluginList>(pluginList.Current.Confirmed);
        pluginList.Apply(new PluginSelectionByNameIntent(confirmed.MembershipVersion, PluginName, true));
    }

    /// <summary>
    ///     Builds a selected-Plugin outcome whose report agrees with the outcomes it is given.
    /// </summary>
    /// <param name="outcomes">One outcome per selected Plugin, in selection order.</param>
    private PluginRunOutcome CreatePluginRunOutcome(params PluginIngestionOutcome[] outcomes)
    {
        var request = new PluginProcessingRunRequest(
            Source(),
            DatabasePath,
            outcomes.Select(static outcome => outcome.PluginName),
            UpdateMode.Append);

        return new PluginRunOutcome(new PluginIngestionReport(request, outcomes));
    }
}
