#nullable enable

using System;
using System.Collections.Generic;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     Records, in order, everything Processing Run Control reports, and can run a test's callback on each entry so a
///     test can re-enter a press from inside a report, as a ViewModel notification handler would.
/// </summary>
/// <remarks>
///     Issue #105, under parent #102. This is the second adapter on the Processing Run Control sink; the User
///     Workflow's ViewModel adapter is the first. Processing Run Control calls its sink only while holding its own
///     lock, so the record needs no synchronization of its own.
/// </remarks>
internal sealed class RecordingProcessingRunControlSink : IProcessingRunControlSink
{
    /// <summary>
    ///     Everything reported so far, in the order Processing Run Control reported it.
    /// </summary>
    public List<ProcessingRunSinkEntry> Entries { get; } = [];

    /// <summary>
    ///     Work run after each entry is recorded, still inside the report that produced it.
    /// </summary>
    public Action<ProcessingRunSinkEntry>? OnEntry { get; set; }

    /// <inheritdoc />
    public void ClearMessageLists()
    {
        Record(new MessageListsCleared());
    }

    /// <inheritdoc />
    public void ReportDefaultedDatabasePath(string defaultedDatabasePath, string? capturedDatabasePath)
    {
        Record(new DefaultedDatabasePathReported(defaultedDatabasePath, capturedDatabasePath));
    }

    /// <inheritdoc />
    public void ReportRunActivity(ActivityProjection runActivity)
    {
        Record(new RunActivityReported(runActivity));
    }

    /// <inheritdoc />
    public void AddMessage(MessageList list, string message)
    {
        Record(new MessageAdded(list, message));
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Recorded as the same entry as <see cref="AddMessage" />, so a test matches a message by its list and text
    ///     whichever member carried it (#147). The per-list members go away with #148.
    /// </remarks>
    public void AddWarningMessage(string message)
    {
        Record(new MessageAdded(MessageList.Warning, message));
    }

    /// <inheritdoc />
    /// <remarks>Recorded as the same entry as <see cref="AddMessage" />; see <see cref="AddWarningMessage" />.</remarks>
    public void AddErrorMessage(string message)
    {
        Record(new MessageAdded(MessageList.Error, message));
    }

    /// <inheritdoc />
    /// <remarks>Recorded as the same entry as <see cref="AddMessage" />; see <see cref="AddWarningMessage" />.</remarks>
    public void AddInformationMessage(string message)
    {
        Record(new MessageAdded(MessageList.Information, message));
    }

    /// <summary>
    ///     Appends one entry and then runs the test's callback for it.
    /// </summary>
    /// <param name="entry">The report just received.</param>
    private void Record(ProcessingRunSinkEntry entry)
    {
        Entries.Add(entry);
        OnEntry?.Invoke(entry);
    }
}

/// <summary>
///     One report Processing Run Control made to its sink.
/// </summary>
internal abstract record ProcessingRunSinkEntry;

/// <summary>
///     Every message list (errors, warnings, and information) was cleared.
/// </summary>
internal sealed record MessageListsCleared : ProcessingRunSinkEntry;

/// <summary>
///     Admission defaulted the database path, reported with the path captured at the press.
/// </summary>
/// <param name="DefaultedDatabasePath">The path admission chose.</param>
/// <param name="CapturedDatabasePath">The database path captured with the press.</param>
internal sealed record DefaultedDatabasePathReported(string DefaultedDatabasePath, string? CapturedDatabasePath)
    : ProcessingRunSinkEntry;

/// <summary>
///     Rendered run activity was reported.
/// </summary>
/// <param name="Activity">The run activity projection.</param>
internal sealed record RunActivityReported(ActivityProjection Activity) : ProcessingRunSinkEntry;

/// <summary>
///     A rendered message was added to a Message List.
/// </summary>
/// <param name="List">The Message List the message was added to.</param>
/// <param name="Message">The rendered message.</param>
internal sealed record MessageAdded(MessageList List, string Message) : ProcessingRunSinkEntry
{
    /// <summary>
    ///     The entry a rendered message is expected to arrive as, so a test takes the list from Presentation's output
    ///     rather than stating it.
    /// </summary>
    /// <param name="rendered">The message Processing Run Presentation rendered.</param>
    /// <returns>The entry recording that message in the list Presentation chose.</returns>
    public static MessageAdded From(RenderedMessage rendered)
    {
        return new MessageAdded(rendered.List, rendered.Text);
    }
}
