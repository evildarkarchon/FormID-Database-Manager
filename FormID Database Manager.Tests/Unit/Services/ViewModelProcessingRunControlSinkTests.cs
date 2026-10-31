#nullable enable

using System;
using System.Collections.Specialized;
using System.Linq;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the Processing Run Control sink adapter: what each report it receives leaves in the ViewModel
///     (ADR-0012, ADR-0014).
/// </summary>
/// <remarks>
///     <para>
///         Issue #120, under parent #118. The adapter is called directly over a real ViewModel with
///         <see cref="SynchronousThreadDispatcher" />, so no admission inputs, Plugin List or executor are involved.
///         When and in what order Processing Run Control reports is pinned by <see cref="ProcessingRunControlTests" />,
///         and run wording by <see cref="ProcessingRunPresentationTests" />; expected text here comes from
///         Presentation's output.
///     </para>
///     <para>
///         The assertions read ViewModel state rather than which ViewModel members were called, and messages are
///         seeded through the adapter itself, so these tests hold however the adapter writes. Change notifications
///         are observed through <see cref="INotifyCollectionChanged" /> for the same reason: it does not depend on the
///         concrete collection type the ViewModel exposes.
///     </para>
/// </remarks>
public sealed class ViewModelProcessingRunControlSinkTests
{
    private const string GameDirectory = @"C:\Games\Skyrim";
    private const string EditedDatabasePath = @"C:\Databases\edited.db";
    private const string DefaultedDatabasePath = @"C:\Defaults\FormID Database Manager\Databases\SkyrimSE.db";

    private readonly ViewModelProcessingRunControlSink _sut;
    private readonly MainWindowViewModel _viewModel = new(new SynchronousThreadDispatcher());

    public ViewModelProcessingRunControlSinkTests()
    {
        _sut = new ViewModelProcessingRunControlSink(_viewModel);
    }

    /// <summary>
    ///     The notifications a press raises before Processing Run Control reports a defaulted database path, each of
    ///     which can carry a re-entrant edit of the database box.
    /// </summary>
    public enum StartupNotification
    {
        /// <summary>The error list being cleared.</summary>
        ErrorListClear,

        /// <summary>The warning list being cleared.</summary>
        WarningListClear,

        /// <summary>The starting status reaching the progress channel.</summary>
        StartingStatus
    }

    /// <summary>
    ///     Verifies the start-over write: the error and warning lists the user was reading are emptied, and the
    ///     information messages stay.
    /// </summary>
    /// <remarks>
    ///     When a press clears is Processing Run Control's rule, pinned by <see cref="ProcessingRunControlTests" />;
    ///     which lists the clear empties is this adapter's write.
    /// </remarks>
    [Fact]
    public void ClearMessageLists_StaleMessagesInEveryList_ClearsErrorsAndWarningsButKeepsInformation()
    {
        _sut.AddErrorMessage("Stale error");
        _sut.AddWarningMessage("Stale warning");
        _sut.AddInformationMessage("Earlier information");

        _sut.ClearMessageLists();

        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Equal(["Earlier information"], _viewModel.InformationMessages);
    }

    /// <summary>
    ///     Verifies that a defaulted database path is written back to the database box while the box still holds the
    ///     path captured with the press.
    /// </summary>
    [Fact]
    public void ReportDefaultedDatabasePath_BoxStillHoldsTheCapture_WritesTheDefault()
    {
        _viewModel.DatabasePath = string.Empty;

        _sut.ReportDefaultedDatabasePath(DefaultedDatabasePath, string.Empty);

        Assert.Equal(DefaultedDatabasePath, _viewModel.DatabasePath);
    }

    /// <summary>
    ///     Verifies the adapter's guard on the defaulted path: an edit made from a notification raised earlier in the
    ///     same press survives the default being reported.
    /// </summary>
    /// <param name="notification">The startup notification whose handler edits the database box.</param>
    /// <remarks>
    ///     Issue #105, under parent #102, and #60. Each case replays the reports Processing Run Control makes for an
    ///     admitted press with no database path, in its order: the list clear, the starting activity, then the
    ///     defaulted path. The edit is raised from a ViewModel notification during one of those sink calls, as a
    ///     re-entrant UI handler would raise it. Writing the default back only while the box still holds the capture
    ///     is this adapter's rule.
    /// </remarks>
    [Theory]
    [InlineData(StartupNotification.ErrorListClear)]
    [InlineData(StartupNotification.WarningListClear)]
    [InlineData(StartupNotification.StartingStatus)]
    public void ReportDefaultedDatabasePath_BoxEditedDuringAStartupNotification_KeepsTheEdit(
        StartupNotification notification)
    {
        _viewModel.DatabasePath = string.Empty;
        var starting = StartingActivity();
        var edited = false;

        void EditDatabasePath()
        {
            edited = true;
            _viewModel.DatabasePath = EditedDatabasePath;
        }

        switch (notification)
        {
            case StartupNotification.ErrorListClear:
                ((INotifyCollectionChanged)_viewModel.ErrorMessages).CollectionChanged += (_, _) => EditDatabasePath();
                break;
            case StartupNotification.WarningListClear:
                ((INotifyCollectionChanged)_viewModel.WarningMessages).CollectionChanged += (_, _) => EditDatabasePath();
                break;
            case StartupNotification.StartingStatus:
                _viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(MainWindowViewModel.ProgressStatus) &&
                        _viewModel.ProgressStatus == starting.Status)
                    {
                        EditDatabasePath();
                    }
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(notification), notification, "Unsupported notification.");
        }

        _sut.ClearMessageLists();
        _sut.ReportRunActivity(starting);
        _sut.ReportDefaultedDatabasePath(DefaultedDatabasePath, string.Empty);

        Assert.True(edited);
        Assert.Equal(EditedDatabasePath, _viewModel.DatabasePath);
    }

    /// <summary>
    ///     Verifies that the run's activity reaches the transient progress channel, and that reporting
    ///     <see cref="ActivityProjection.None" /> hands the channel back.
    /// </summary>
    [Fact]
    public void ReportRunActivity_StartingThenNone_ShowsTheRunThenHandsTheChannelBack()
    {
        var starting = StartingActivity();

        _sut.ReportRunActivity(starting);

        Assert.True(_viewModel.IsProgressVisible);
        Assert.Equal(starting.Status, _viewModel.ProgressStatus);

        _sut.ReportRunActivity(ActivityProjection.None);

        Assert.False(_viewModel.IsProgressVisible);
        Assert.Empty(_viewModel.ProgressStatus);
    }

    /// <summary>
    ///     Verifies where each message part of a rendered run report lands when one run produced both: the warning
    ///     messages in the warning list and the failure messages in the error list, with the information list untouched.
    /// </summary>
    /// <remarks>
    ///     Each part is handed to the sink member Processing Run Control uses for it, as it does for a finished run.
    ///     What is asserted is which list each part reached; the order they are reported in is pinned by
    ///     <see cref="ProcessingRunControlTests" />.
    /// </remarks>
    [Fact]
    public void AddWarningAndErrorMessages_RenderedRunReport_RouteEachPartToItsOwnList()
    {
        var rendered = ProcessingRunPresentation.Render(CreatePluginRunOutcome(
            new IngestedPlugin("User.esp", 1, new ProcessingWarning(1, ["Recoverable issue"])),
            new FailedPlugin(
                "Bad.esp",
                new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Invalid plugin header."))));
        Assert.NotEmpty(rendered.WarningMessages);
        Assert.NotEmpty(rendered.ErrorMessages);
        Assert.Empty(rendered.InformationMessages);

        ReportMessages(rendered);

        Assert.Equal(rendered.WarningMessages.ToArray(), _viewModel.WarningMessages);
        Assert.Equal(rendered.ErrorMessages.ToArray(), _viewModel.ErrorMessages);
        Assert.Empty(_viewModel.InformationMessages);
    }

    /// <summary>
    ///     Verifies that a dry run's rendered plan lands in the information list the user reads, and nowhere else.
    /// </summary>
    [Fact]
    public void AddInformationMessage_RenderedDryRunPlan_AddsThePlanToTheInformationList()
    {
        var rendered = ProcessingRunPresentation.Render(new PlannedRunOutcome(new PluginRunPlan(
            new PluginIngestionPlan(
                new PluginProcessingRunRequest(
                    PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
                    string.Empty,
                    ["User.esp", "Absent.esp"],
                    UpdateMode.Append,
                    dryRun: true),
                [
                    new PlannedPluginIngestion("User.esp"),
                    new PlannedPluginSkip("Absent.esp", PlannedSkipReason.NotPresentInLoadOrder)
                ]))));
        Assert.NotEmpty(rendered.InformationMessages);

        ReportMessages(rendered);

        Assert.Equal(rendered.InformationMessages.ToArray(), _viewModel.InformationMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Empty(_viewModel.ErrorMessages);
    }

    /// <summary>
    ///     Hands every message of a rendered run report to the sink member for its list.
    /// </summary>
    /// <param name="rendered">The rendered report.</param>
    private void ReportMessages(RenderedRunReport rendered)
    {
        foreach (var message in rendered.WarningMessages)
        {
            _sut.AddWarningMessage(message);
        }

        foreach (var message in rendered.ErrorMessages)
        {
            _sut.AddErrorMessage(message);
        }

        foreach (var message in rendered.InformationMessages)
        {
            _sut.AddInformationMessage(message);
        }
    }

    /// <summary>
    ///     The active starting activity Processing Run Control reports for an admitted press, built from Processing Run
    ///     Presentation's starting status so no wording is restated here.
    /// </summary>
    private static ActivityProjection StartingActivity()
    {
        var starting = ProcessingRunPresentation.Render(new ProcessingRunStarting());
        return new ActivityProjection(true, starting.Status, starting.Value ?? 0);
    }

    /// <summary>
    ///     Builds a selected-Plugin outcome whose report agrees with the outcomes it is given.
    /// </summary>
    /// <param name="outcomes">One outcome per selected Plugin, in selection order.</param>
    /// <returns>A completed selected-Plugin outcome carrying that report.</returns>
    /// <remarks>
    ///     A report validates its outcomes against the selection they came from, so the request is rebuilt from the
    ///     outcomes' Plugin names.
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
}
