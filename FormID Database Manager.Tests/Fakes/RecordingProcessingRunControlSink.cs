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
    public void AddWarningMessage(string message)
    {
        Record(new WarningMessageAdded(message));
    }

    /// <inheritdoc />
    public void AddErrorMessage(string message)
    {
        Record(new ErrorMessageAdded(message));
    }

    /// <inheritdoc />
    public void AddInformationMessage(string message)
    {
        Record(new InformationMessageAdded(message));
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
///     The error and warning message lists were cleared.
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
///     A rendered warning message was reported.
/// </summary>
/// <param name="Message">The rendered message.</param>
internal sealed record WarningMessageAdded(string Message) : ProcessingRunSinkEntry;

/// <summary>
///     A rendered error message was reported.
/// </summary>
/// <param name="Message">The rendered message.</param>
internal sealed record ErrorMessageAdded(string Message) : ProcessingRunSinkEntry;

/// <summary>
///     A rendered information message was reported.
/// </summary>
/// <param name="Message">The rendered message.</param>
internal sealed record InformationMessageAdded(string Message) : ProcessingRunSinkEntry;
