#nullable enable

using System;
using System.Collections.Generic;
using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     Records, in one ordered log, every state and notice the Game Context reports, and can run a test's callback on
///     each entry so a test can re-enter a Game Context intent from inside a report, as a ComboBox echo would.
/// </summary>
/// <remarks>
///     Issue #116, under parent #114. This is the second adapter on the Game Context sink; the User Workflow's ViewModel
///     adapter is the first (ADR-0013). The sink contract is UI-thread-affine, and the tests that use this recorder
///     sequence every transition with awaits, gates and completion sources, so no two reports overlap and the log needs
///     no synchronization of its own.
/// </remarks>
internal sealed class RecordingGameContextSink : IGameContextSink
{
    /// <summary>
    ///     Everything reported so far, in the order the Game Context reported it.
    /// </summary>
    public List<GameContextSinkEntry> Entries { get; } = [];

    /// <summary>
    ///     Work run after each entry is recorded, still inside the report that produced it.
    /// </summary>
    public Action<GameContextSinkEntry>? OnEntry { get; set; }

    /// <inheritdoc />
    public void ReportState(GameContextState state)
    {
        Record(new StateReported(state));
    }

    /// <inheritdoc />
    public void ReportNotice(GameContextNotice notice)
    {
        Record(new NoticeReported(notice));
    }

    /// <summary>
    ///     Appends one entry and then runs the test's callback for it.
    /// </summary>
    /// <param name="entry">The report just received.</param>
    private void Record(GameContextSinkEntry entry)
    {
        Entries.Add(entry);
        OnEntry?.Invoke(entry);
    }
}

/// <summary>
///     One report the Game Context made to its sink.
/// </summary>
internal abstract record GameContextSinkEntry;

/// <summary>
///     A complete Game Context state was reported.
/// </summary>
/// <param name="State">The reported state.</param>
/// <remarks>
///     Record equality on this entry compares the state's available directories by reference, because they are an
///     <c>ImmutableArray</c>, so tests compare reported states field by field rather than with this record's equality.
/// </remarks>
internal sealed record StateReported(GameContextState State) : GameContextSinkEntry;

/// <summary>
///     A Game Context notice was reported.
/// </summary>
/// <param name="Notice">The reported notice, a value record that compares by content.</param>
internal sealed record NoticeReported(GameContextNotice Notice) : GameContextSinkEntry;
