#nullable enable

using System;
using System.Collections.Specialized;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
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

        /// <summary>The information list being cleared.</summary>
        InformationListClear,

        /// <summary>The starting status reaching the progress channel.</summary>
        StartingStatus
    }

    /// <summary>
    ///     Verifies the start-over write: every Message List the user was reading is emptied, information included, so
    ///     what remains afterwards is only what the new press produces (#95).
    /// </summary>
    /// <remarks>
    ///     When a press clears is Processing Run Control's rule, pinned by <see cref="ProcessingRunControlTests" />;
    ///     which lists the clear empties is this adapter's write.
    /// </remarks>
    [Fact]
    public void ClearMessageLists_StaleMessagesInEveryList_ClearsEveryList()
    {
        _sut.AddMessage(MessageList.Error, "Stale error");
        _sut.AddMessage(MessageList.Warning, "Stale warning");
        _sut.AddMessage(MessageList.Information, "Earlier information");

        _sut.ClearMessageLists();

        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Empty(_viewModel.InformationMessages);
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
    [InlineData(StartupNotification.InformationListClear)]
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
            case StartupNotification.InformationListClear:
                ((INotifyCollectionChanged)_viewModel.InformationMessages).CollectionChanged +=
                    (_, _) => EditDatabasePath();
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
    ///     The name of every Message List, so the pass-through test covers a list the day it is added.
    /// </summary>
    /// <remarks>
    ///     Names rather than values because <see cref="MessageList" /> is internal and a public test method cannot take
    ///     it as a parameter.
    /// </remarks>
    public static TheoryData<string> EveryMessageListName => new(Enum.GetNames<MessageList>());

    /// <summary>
    ///     Verifies that the add-message member writes the text to exactly the Message List it was handed, with no
    ///     per-list mapping of its own, and leaves every other list alone (ADR-0014).
    /// </summary>
    /// <remarks>
    ///     Issue #147. Which list a run message belongs to is Processing Run Presentation's choice, so the adapter's
    ///     only behaviour is passing that choice through; the test text is arbitrary for the same reason.
    /// </remarks>
    /// <param name="listName">The name of the Message List to write to.</param>
    [Theory]
    [MemberData(nameof(EveryMessageListName))]
    public void AddMessage_EachMessageList_AddsTheTextToThatListOnly(string listName)
    {
        var list = Enum.Parse<MessageList>(listName);
        const string text = "Rendered run message";

        _sut.AddMessage(list, text);

        Assert.Equal(list == MessageList.Error ? [text] : Array.Empty<string>(), _viewModel.ErrorMessages);
        Assert.Equal(list == MessageList.Warning ? [text] : Array.Empty<string>(), _viewModel.WarningMessages);
        Assert.Equal(list == MessageList.Information ? [text] : Array.Empty<string>(), _viewModel.InformationMessages);
    }

    /// <summary>
    ///     Verifies that a Message List the ViewModel does not define is refused before anything is written, the same
    ///     failure mode as the Game Context sink adapter's.
    /// </summary>
    [Fact]
    public void AddMessage_UndefinedMessageList_ThrowsAndWritesNothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sut.AddMessage((MessageList)999, "Unroutable"));

        Assert.Empty(_viewModel.ErrorMessages);
        Assert.Empty(_viewModel.WarningMessages);
        Assert.Empty(_viewModel.InformationMessages);
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
}
