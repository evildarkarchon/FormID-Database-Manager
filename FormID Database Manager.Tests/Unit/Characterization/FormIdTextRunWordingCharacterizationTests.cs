#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Characterization;

/// <summary>
///     Pins every user-facing string and progress value a FormID text-file Processing Run produces today, including the
///     ones the FormID Record Store emits into the run's own progress channel.
/// </summary>
/// <remarks>
///     <para>
///         Issue #63, under parent #61. The run is driven end to end through the production composition, because the
///         Store — not the run — owns four of the five strings a text run shows, and stubbing it out would pin nothing.
///     </para>
///     <para>
///         The run's terminal status is one of the two strings parent #61 changes on purpose: it has gained the Plugin
///         and record counts a selected-Plugin run already reported, replacing a flat "Processing completed
///         successfully!" that discarded them. Every other string here is pinned unchanged.
///     </para>
/// </remarks>
[Collection("Database Tests")]
public sealed class FormIdTextRunWordingCharacterizationTests : IDisposable
{
    /// <summary>
    ///     The number of valid rows written to the text file, chosen so the Store's every-thousandth-record report
    ///     fires twice with percentages that are exact in binary and therefore comparable by value.
    /// </summary>
    /// <remarks>
    ///     Each row is <see cref="RowByteLength" /> bytes, so the file is exactly 65,536 bytes and the Store's reader
    ///     has pulled 32,768 bytes by row 1,000 and 64,512 bytes by row 2,000 — 50% and 98.4375% of the file. See
    ///     <see cref="WriteFormIdTextFileAsync" /> for why the reader's position, not the row's, is what the Store
    ///     reports.
    /// </remarks>
    private const int RecordCount = 2048;

    /// <summary>
    ///     The exact byte length of every written row, counting the CRLF terminator.
    /// </summary>
    private const int RowByteLength = 32;

    private const string FirstPluginName = "PluginA.esp";
    private const string SecondPluginName = "PluginB.esp";

    private readonly string _workingDirectory;

    /// <summary>
    ///     Creates an isolated working directory for this test's database and FormID text file.
    /// </summary>
    public FormIdTextRunWordingCharacterizationTests()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), $"formid_text_wording_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workingDirectory);
    }

    /// <summary>
    ///     Releases SQLite file handles and removes the isolated working directory.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_workingDirectory))
            {
                Directory.Delete(_workingDirectory, recursive: true);
            }
        }
        catch
        {
            // Cleanup is best-effort: a retained SQLite handle or antivirus scan must not fail the characterization.
        }
    }

    /// <summary>
    ///     Pins the complete ordered report of an appending FormID text run: the Store's starting report, both of its
    ///     every-thousandth-record percentage reports, its completion report, and the run's own terminal status.
    /// </summary>
    /// <remarks>
    ///     Append mode names no Plugin, which is the whole difference between this report and the replacing one below.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_FormIdTextRunInAppendMode_ReportsTheStoreAndRunStringsWithoutNamingAPlugin()
    {
        var report = await ExecuteTextRunAsync(UpdateMode.Append);

        Assert.Equal(
            [
                ProcessingRunEvent.Status("Starting processing...", 0),
                ProcessingRunEvent.Status("Processing: 50.0% (1,000 records)", 50),
                ProcessingRunEvent.Status("Processing: 98.4% (2,000 records)", 98.4375),
                ProcessingRunEvent.Status("Completed processing 2 plugins (2,048 total records)", 100)
            ],
            report.Events);
        AssertTerminalCompletion(report.Rendered);
    }

    /// <summary>
    ///     Pins the same report under the update mode that replaces existing Plugin rows, which additionally names each
    ///     Plugin the first time the file mentions it, without a progress value — and pins where those named reports
    ///     fall relative to the percentage reports.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FormIdTextRunInReplaceMode_AlsoNamesEachPluginTheFirstTimeItAppears()
    {
        var report = await ExecuteTextRunAsync(UpdateMode.ReplacePluginRecords);

        Assert.Equal(
            [
                ProcessingRunEvent.Status("Starting processing...", 0),
                ProcessingRunEvent.Status($"Processing plugin: {FirstPluginName}"),
                ProcessingRunEvent.Status("Processing: 50.0% (1,000 records)", 50),
                ProcessingRunEvent.Status($"Processing plugin: {SecondPluginName}"),
                ProcessingRunEvent.Status("Processing: 98.4% (2,000 records)", 98.4375),
                ProcessingRunEvent.Status("Completed processing 2 plugins (2,048 total records)", 100)
            ],
            report.Events);
        AssertTerminalCompletion(report.Rendered);
    }

    /// <summary>
    ///     Asserts the terminal completion a text run renders, which is one of the two strings parent #61 changes on
    ///     purpose: it gains the Plugin and record counts a selected-Plugin run has always reported, instead of the
    ///     flat "Processing completed successfully!" that discarded them.
    /// </summary>
    /// <param name="rendered">The report rendered from how the run ended.</param>
    private static void AssertTerminalCompletion(RenderedRunReport rendered)
    {
        Assert.Equal(
            new ActivityProjection(true, "Processing completed successfully: 2 Plugins and 2,048 records.", 100),
            rendered.Activity);
        Assert.Empty(rendered.WarningMessages);
        Assert.Empty(rendered.ErrorMessages);
        Assert.Empty(rendered.InformationMessages);
    }

    /// <summary>
    ///     Executes one FormID text-file Processing Run through the production composition and collects everything it
    ///     said.
    /// </summary>
    /// <param name="updateMode">The update mode the run applies to the Plugins named in the text file.</param>
    /// <returns>The transient events in report order, and the report rendered from how the run ended.</returns>
    /// <remarks>
    ///     The run and its rendering both happen under the invariant culture because the Store's strings and the run's
    ///     own completion status format numbers with the ambient culture, and the wording being pinned here should not
    ///     depend on the machine's regional settings. <see cref="CultureInfo.CurrentCulture" /> is an
    ///     <c>AsyncLocal</c>, so the assignment reaches the run's worker thread and no test outside this asynchronous
    ///     flow.
    /// </remarks>
    private async Task<TextRunReport> ExecuteTextRunAsync(UpdateMode updateMode)
    {
        var textFilePath = await WriteFormIdTextFileAsync();
        var databasePath = Path.Combine(_workingDirectory, "formids.db");
        var events = new List<ProcessingRunEvent>();
        var request = new FormIdTextProcessingRunRequest(
            textFilePath,
            databasePath,
            GameRelease.SkyrimSE,
            updateMode);

        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            using var sut = new ProcessingRunExecutor();
            var outcome = await sut.ExecuteAsync(request, new SynchronousProgress<ProcessingRunEvent>(events.Add));
            return new TextRunReport(events, ProcessingRunPresentation.Render(outcome));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    ///     Everything one FormID text-file Processing Run said.
    /// </summary>
    /// <param name="Events">The reported run events, in report order.</param>
    /// <param name="Rendered">The report rendered from the run's outcome.</param>
    private readonly record struct TextRunReport(
        IReadOnlyList<ProcessingRunEvent> Events,
        RenderedRunReport Rendered);

    /// <summary>
    ///     Writes a pipe-delimited FormID text file of fixed-width rows, split evenly between two Plugins so both are
    ///     named in replace mode.
    /// </summary>
    /// <returns>The path of the written text file.</returns>
    /// <remarks>
    ///     <para>
    ///         The rows are written with an explicit CRLF rather than through <c>File.WriteAllLines</c>, so the file is
    ///         exactly <see cref="RecordCount" /> × <see cref="RowByteLength" /> bytes on any platform. Every character
    ///         is ASCII, so one character is one UTF-8 byte.
    ///     </para>
    ///     <para>
    ///         That fixed width is what makes the pinned percentages exact. The Store derives them from the stream
    ///         position, which is how far the <c>StreamReader</c> has pulled ahead of the row it just returned, not
    ///         where that row ends — so the reported percentage is a function of the reader's 1 KiB read size as well
    ///         as the file's size. The widths here are chosen so both reported ratios land on exact binary fractions;
    ///         a report that reads 50.0% is therefore a real assertion about the Store's arithmetic, not a rounding
    ///         coincidence.
    ///     </para>
    /// </remarks>
    private async Task<string> WriteFormIdTextFileAsync()
    {
        var textFilePath = Path.Combine(_workingDirectory, "formids.txt");
        var contents = new StringBuilder(RecordCount * RowByteLength);
        for (var index = 0; index < RecordCount; index++)
        {
            var pluginName = index < RecordCount / 2 ? FirstPluginName : SecondPluginName;
            var row = $"{pluginName}|{index:X8}|Entry{index:D4}";
            Assert.Equal(RowByteLength - 2, row.Length);
            contents.Append(row).Append("\r\n");
        }

        await File.WriteAllTextAsync(
            textFilePath,
            contents.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            TestContext.Current.CancellationToken);
        Assert.Equal(RecordCount * RowByteLength, new FileInfo(textFilePath).Length);
        return textFilePath;
    }
}
