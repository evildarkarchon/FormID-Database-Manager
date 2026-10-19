#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using FormID_Database_Manager.TestUtilities.Mocks;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers FormID Text Ingestion through its interface: the rows it hands the text-row writer role, the progress it
///     reports and in what order, the counts it returns, and the Dry Run plan it builds.
/// </summary>
/// <remarks>
///     Issue #110, under parent #108. These tests use temporary files and a recording text-row writer, and no SQLite:
///     staging and per-Plugin commits are the FormID Record Store's and are tested there. The parsing and progress
///     tests moved here from the FormID Record Store tests, whose text-file import operation was retired. The plan
///     tests moved here from the executor tests, which now run against a fake FormID Text Ingestion.
/// </remarks>
public sealed class FormIdTextIngestionTests : IDisposable
{
    private readonly string _testFilesDirectory =
        Path.Combine(Path.GetTempPath(), $"formid_text_ingestion_{Guid.NewGuid():N}");

    /// <summary>
    ///     Creates the isolated directory for this test's FormID text files.
    /// </summary>
    public FormIdTextIngestionTests()
    {
        Directory.CreateDirectory(_testFilesDirectory);
    }

    /// <summary>
    ///     Removes the isolated directory and every FormID text file the test wrote.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_testFilesDirectory, recursive: true);
        }
        catch
        {
            // Cleanup is best-effort: antivirus or a lingering handle must not fail the test that already ran.
        }
    }

    /// <summary>
    ///     Verifies that interleaved valid rows for several Plugins reach the writer in file order, with the request's
    ///     Update Mode, and that the returned counts are distinct Plugins and valid rows.
    /// </summary>
    [Fact]
    public async Task IngestAsync_ValidRowsForSeveralPlugins_HandsEveryRowToTheWriterInFileOrder()
    {
        var path = await WriteFormIdTextFileAsync(
            "valid_multiple_plugins.txt",
            [
                "Plugin1.esp|000001|Entry1",
                "Plugin1.esp|000002|Entry2",
                "Plugin2.esp|000003|Entry3",
                "Plugin2.esp|000004|Entry4",
                "Plugin1.esp|000005|Entry5",
                "Plugin3.esp|000006|Entry6"
            ]);
        var writer = new RecordingFormIdTextRowWriter();
        var sut = new FormIdTextIngestion();

        var result = await sut.IngestAsync(
            CreateRequest(path, UpdateMode.ReplacePluginRecords),
            writer,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(3, 6), result);
        Assert.Equal(1, writer.CallCount);
        Assert.Equal(UpdateMode.ReplacePluginRecords, writer.UpdateMode);
        Assert.Equal(
            [
                new FormIdTextRow("Plugin1.esp", "000001", "Entry1"),
                new FormIdTextRow("Plugin1.esp", "000002", "Entry2"),
                new FormIdTextRow("Plugin2.esp", "000003", "Entry3"),
                new FormIdTextRow("Plugin2.esp", "000004", "Entry4"),
                new FormIdTextRow("Plugin1.esp", "000005", "Entry5"),
                new FormIdTextRow("Plugin3.esp", "000006", "Entry6")
            ],
            writer.Rows);
    }

    /// <summary>
    ///     Verifies that blank, whitespace-only, and malformed rows (no pipe, one pipe, more than two pipes) are skipped
    ///     without hiding the valid rows around them, and that valid fields are trimmed.
    /// </summary>
    [Fact]
    public async Task IngestAsync_MalformedBlankAndWhitespaceRows_HandsOnlyValidTrimmedRows()
    {
        var path = await WriteFormIdTextFileAsync(
            "malformed_rows.txt",
            [
                "Plugin1.esp|000001|Entry1",
                "  Plugin2.esp  |  000002  |  Entry2  ",
                "Plugin3.esp|000003|Entry3|ExtraData",
                "",
                "   ",
                "InvalidLine",
                "Plugin4.esp|000004",
                "||||"
            ]);
        var writer = new RecordingFormIdTextRowWriter();
        var sut = new FormIdTextIngestion();

        var result = await sut.IngestAsync(
            CreateRequest(path),
            writer,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(2, 2), result);
        Assert.Equal(
            [
                new FormIdTextRow("Plugin1.esp", "000001", "Entry1"),
                new FormIdTextRow("Plugin2.esp", "000002", "Entry2")
            ],
            writer.Rows);
    }

    /// <summary>
    ///     Verifies that punctuation inside Entries reaches the writer unchanged, while the surrounding whitespace of
    ///     every field is trimmed.
    /// </summary>
    [Fact]
    public async Task IngestAsync_SpecialCharactersAndPaddedFields_HandsTrimmedFieldsWithEntriesPreserved()
    {
        string[] expectedEntries =
        [
            "Entry with spaces",
            "Entry-with-dashes",
            "Entry_with_underscores",
            "Entry.with.dots",
            "Entry'with'quotes",
            @"Entry\with\backslashes",
            "Entry/with/forward/slashes",
            "Entry(with)parentheses",
            "Entry[with]brackets",
            "Entry{with}braces"
        ];
        var lines = expectedEntries.Select((entry, index) => $" Plugin.esp |\t{index + 1:X6} | {entry}\t");
        var path = await WriteFormIdTextFileAsync("special_characters.txt", lines);
        var writer = new RecordingFormIdTextRowWriter();
        var sut = new FormIdTextIngestion();

        var result = await sut.IngestAsync(
            CreateRequest(path),
            writer,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(1, expectedEntries.Length), result);
        Assert.Equal(
            expectedEntries.Select((entry, index) => new FormIdTextRow("Plugin.esp", $"{index + 1:X6}", entry)),
            writer.Rows);
    }

    /// <summary>
    ///     Verifies that an empty file is opened and completes with zero counts and no rows.
    /// </summary>
    [Fact]
    public async Task IngestAsync_EmptyFile_ReturnsZeroCountsAndHandsNoRows()
    {
        var path = await WriteFormIdTextFileAsync("empty.txt", []);
        var writer = new RecordingFormIdTextRowWriter();
        var sut = new FormIdTextIngestion();

        var result = await sut.IngestAsync(
            CreateRequest(path),
            writer,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(0, 0), result);
        Assert.Empty(writer.Rows);
    }

    /// <summary>
    ///     Verifies that a missing file fails ingestion with the file-system error, before anything is reported or
    ///     handed to the Store.
    /// </summary>
    [Fact]
    public async Task IngestAsync_MissingFile_ThrowsFileNotFoundExceptionBeforeReportingOrWriting()
    {
        var path = Path.Combine(_testFilesDirectory, "missing.txt");
        var reports = new List<FormIdTextIngestionProgress>();
        var writer = new RecordingFormIdTextRowWriter();
        var sut = new FormIdTextIngestion();

        await Assert.ThrowsAsync<FileNotFoundException>(() => sut.IngestAsync(
            CreateRequest(path),
            writer,
            new SynchronousProgress<FormIdTextIngestionProgress>(reports.Add),
            TestContext.Current.CancellationToken));

        Assert.Empty(reports);
        Assert.Equal(0, writer.CallCount);
    }

    /// <summary>
    ///     Verifies that rows naming one Plugin in different casings count as one Plugin, that each row keeps its own
    ///     casing, and that the Plugin is reported once with the casing of its first valid row.
    /// </summary>
    [Fact]
    public async Task IngestAsync_PluginInSeveralCasings_CountsOnePluginAndReportsTheFirstRowsCasing()
    {
        var path = await WriteFormIdTextFileAsync(
            "casings.txt",
            [
                "not a row",
                "First.esp|000001|FirstEntry",
                "FIRST.ESP|000002|SecondEntry",
                "first.esp|000003|ThirdEntry"
            ]);
        var reports = new List<FormIdTextIngestionProgress>();
        var writer = new RecordingFormIdTextRowWriter();
        var sut = new FormIdTextIngestion();

        var result = await sut.IngestAsync(
            CreateRequest(path),
            writer,
            new SynchronousProgress<FormIdTextIngestionProgress>(reports.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(1, 3), result);
        Assert.Equal(["First.esp", "FIRST.ESP", "first.esp"], writer.Rows.Select(row => row.Plugin));
        Assert.Equal(
            "First.esp",
            Assert.Single(reports.OfType<FormIdTextIngestionPluginFirstEncountered>()).PluginName);
    }

    /// <summary>
    ///     Verifies the progress reported across several intervals: the opening first and once, carrying the file's
    ///     size; the Plugin's first encounter; and a counter update every thousandth valid row, with counters that only
    ///     advance.
    /// </summary>
    /// <remarks>
    ///     FormID Text Ingestion reports facts, not sentences, so this pins the cases and numbers rather than any
    ///     wording. The final counts are not a progress report at all — they are the value ingestion returns.
    /// </remarks>
    [Fact]
    public async Task IngestAsync_CrossesProgressIntervals_ReportsOpeningOnceThenMonotonicCounterUpdates()
    {
        const int totalRecords = 2500;
        var path = await WriteFormIdTextFileAsync(
            "progress.txt",
            Enumerable.Range(0, totalRecords).Select(i => $"Plugin.esp|{i:X6}|Entry{i}"));
        var totalBytes = new FileInfo(path).Length;
        var reports = new List<FormIdTextIngestionProgress>();
        var sut = new FormIdTextIngestion();

        var result = await sut.IngestAsync(
            CreateRequest(path),
            new RecordingFormIdTextRowWriter(),
            new SynchronousProgress<FormIdTextIngestionProgress>(reports.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(1, totalRecords), result);
        Assert.Equal(new FormIdTextIngestionOpening(totalBytes), reports[0]);
        Assert.Single(reports.OfType<FormIdTextIngestionOpening>());
        Assert.Collection(
            reports.Skip(1),
            report => Assert.Equal(
                "Plugin.esp",
                Assert.IsType<FormIdTextIngestionPluginFirstEncountered>(report).PluginName),
            report => Assert.Equal(1000L, Assert.IsType<FormIdTextIngestionCounterUpdate>(report).RecordCount),
            report => Assert.Equal(2000L, Assert.IsType<FormIdTextIngestionCounterUpdate>(report).RecordCount));

        var counterUpdates = reports.OfType<FormIdTextIngestionCounterUpdate>().ToArray();
        Assert.All(counterUpdates, update => Assert.Equal(totalBytes, update.TotalBytes));
        Assert.All(counterUpdates, update => Assert.InRange(update.BytesRead, 1L, totalBytes));
        Assert.True(counterUpdates[1].BytesRead >= counterUpdates[0].BytesRead);
    }

    /// <summary>
    ///     Verifies the exact byte counts reported for a file of known size, pinning the stream-position arithmetic
    ///     every rendered percentage is derived from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Issue #68. The monotonicity assertion above bounds these counts but does not pin them, and a caller
    ///         renders them as a percentage: an off-by-a-buffer error would keep every count in range and in order
    ///         while showing the user the wrong number. Fixed-width rows are what make the expected values exact —
    ///         with 2,048 rows of 32 bytes the file is exactly 65,536 bytes, so the two interval reports land on
    ///         ratios that are exact in binary and comparable by value rather than by tolerance.
    ///     </para>
    ///     <para>
    ///         What is reported is how far the <c>StreamReader</c> has pulled ahead of the row it just returned, not
    ///         where that row ends, so these counts are a function of the reader's 1 KiB read size as well as the
    ///         file's size. That is precisely why they are worth pinning: the counts are not derivable from the row
    ///         number by anyone reading the caller.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task IngestAsync_FileOfKnownSize_ReportsTheExactReaderPositionAtEachInterval()
    {
        const int recordCount = 2048;
        const int rowByteLength = 32;
        const long totalBytes = recordCount * rowByteLength;
        var path = await WriteFixedWidthFormIdTextFileAsync("known_size.txt", recordCount, rowByteLength);
        var reports = new List<FormIdTextIngestionProgress>();
        var sut = new FormIdTextIngestion();

        var result = await sut.IngestAsync(
            CreateRequest(path),
            new RecordingFormIdTextRowWriter(),
            new SynchronousProgress<FormIdTextIngestionProgress>(reports.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(2, recordCount), result);
        Assert.Equal(totalBytes, new FileInfo(path).Length);
        // Half the file by the thousandth record, and all but one 1 KiB read by the two-thousandth.
        Assert.Equal(
            [
                new FormIdTextIngestionCounterUpdate(1_000, 32_768L, totalBytes),
                new FormIdTextIngestionCounterUpdate(2_000, 64_512L, totalBytes)
            ],
            reports.OfType<FormIdTextIngestionCounterUpdate>());
    }

    /// <summary>
    ///     Verifies that every Plugin is reported the first time the file mentions it, in file order and identically
    ///     under both Update Modes, each before its first row reaches the writer.
    /// </summary>
    /// <param name="updateMode">The Update Mode the request carries.</param>
    /// <remarks>
    ///     Running both modes is the point: whether a newly seen Plugin is worth naming to the user is the Processing
    ///     Run's decision, so FormID Text Ingestion's reports must not vary with the Update Mode at all.
    /// </remarks>
    [Theory]
    [InlineData(UpdateMode.Append)]
    [InlineData(UpdateMode.ReplacePluginRecords)]
    public async Task IngestAsync_SeveralPlugins_ReportsEachPluginOnFirstEncounterBeforeItsFirstRow(
        UpdateMode updateMode)
    {
        var path = await WriteFormIdTextFileAsync(
            "first_encounters.txt",
            [
                "First.esp|000001|FirstEntry",
                "FIRST.ESP|000002|SecondEntry",
                "Second.esp|000003|ThirdEntry",
                "first.esp|000004|FourthEntry"
            ]);
        var events = new List<string>();
        var writer = new RecordingFormIdTextRowWriter { OnRow = row => events.Add($"row {row.FormId}") };
        var progress = new SynchronousProgress<FormIdTextIngestionProgress>(report => events.Add(report switch
        {
            FormIdTextIngestionOpening => "opening",
            FormIdTextIngestionPluginFirstEncountered plugin => $"plugin {plugin.PluginName} at {plugin.RecordCount}",
            _ => report.ToString()
        }));
        var sut = new FormIdTextIngestion();

        var result = await sut.IngestAsync(
            CreateRequest(path, updateMode),
            writer,
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(2, 4), result);
        Assert.Equal(
            [
                "opening",
                "plugin First.esp at 1",
                "row 000001",
                "row 000002",
                "plugin Second.esp at 3",
                "row 000003",
                "row 000004"
            ],
            events);
    }

    /// <summary>
    ///     Verifies that when a Plugin's first row lands exactly on a progress interval, the counter update comes first,
    ///     then the Plugin, and both precede that row reaching the writer.
    /// </summary>
    /// <remarks>
    ///     The two facts have identical counters but distinct meanings. Swapping or collapsing them would change what
    ///     the user sees on this collision (ADR-0007, 2026-09-16).
    /// </remarks>
    [Fact]
    public async Task IngestAsync_NewPluginOnAProgressInterval_ReportsTheCounterUpdateBeforeThePlugin()
    {
        const int intervalRecord = 1000;
        var path = await WriteFormIdTextFileAsync(
            "plugin_on_interval.txt",
            Enumerable
                .Range(0, intervalRecord)
                .Select(index => index < intervalRecord - 1
                    ? $"First.esp|{index:X6}|Entry{index}"
                    : $"Second.esp|{index:X6}|Entry{index}"));
        var reports = new List<FormIdTextIngestionProgress>();
        var reportsBeforeLastRow = -1;
        var writer = new RecordingFormIdTextRowWriter
        {
            OnRow = row =>
            {
                if (row.Plugin == "Second.esp")
                {
                    reportsBeforeLastRow = reports.Count;
                }
            }
        };
        var sut = new FormIdTextIngestion();

        var result = await sut.IngestAsync(
            CreateRequest(path, UpdateMode.ReplacePluginRecords),
            writer,
            new SynchronousProgress<FormIdTextIngestionProgress>(reports.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextFileImportResult(2, intervalRecord), result);
        Assert.Equal(4, reports.Count);
        Assert.IsType<FormIdTextIngestionOpening>(reports[0]);
        Assert.Equal("First.esp", Assert.IsType<FormIdTextIngestionPluginFirstEncountered>(reports[1]).PluginName);
        var counterUpdate = Assert.IsType<FormIdTextIngestionCounterUpdate>(reports[2]);
        var secondPlugin = Assert.IsType<FormIdTextIngestionPluginFirstEncountered>(reports[3]);
        Assert.Equal(
            new FormIdTextIngestionPluginFirstEncountered(
                intervalRecord,
                counterUpdate.BytesRead,
                counterUpdate.TotalBytes,
                "Second.esp"),
            secondPlugin);
        Assert.Equal(intervalRecord, counterUpdate.RecordCount);
        Assert.Equal(4, reportsBeforeLastRow);
    }

    /// <summary>
    ///     Verifies that cancelling while the writer is consuming the stream stops ingestion: no further row is read,
    ///     and the cancellation propagates rather than returning counts.
    /// </summary>
    [Fact]
    public async Task IngestAsync_CancelledWhileTheWriterConsumes_StopsReadingAndThrowsOperationCanceled()
    {
        var path = await WriteFormIdTextFileAsync(
            "cancel.txt",
            Enumerable.Range(0, 10).Select(i => $"Plugin.esp|{i:X6}|Entry{i}"));
        using var cancellation = new CancellationTokenSource();
        var writer = new RecordingFormIdTextRowWriter
        {
            OnRow = row =>
            {
                if (row.FormId == "000002")
                {
                    cancellation.Cancel();
                }
            }
        };
        var sut = new FormIdTextIngestion();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.IngestAsync(CreateRequest(path), writer, cancellationToken: cancellation.Token));

        Assert.Equal(["000000", "000001", "000002"], writer.Rows.Select(row => row.FormId));
    }

    /// <summary>
    ///     Verifies that a Dry Run plan reports a present file's path and exact size without opening it.
    /// </summary>
    /// <remarks>
    ///     The file is held open with no sharing while the plan is built, so a plan that opened the file would fail
    ///     rather than pass.
    /// </remarks>
    [Fact]
    public async Task PlanAsync_PresentFile_ReportsItsPathAndSizeWithoutOpeningIt()
    {
        var path = await WriteFormIdTextFileAsync("present.txt", ["Skyrim.esm|000001|Entry"]);
        var expectedSize = new FileInfo(path).Length;
        var sut = new FormIdTextIngestion();

        FormIdTextRunPlan plan;
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            plan = await sut.PlanAsync(CreateRequest(path, dryRun: true), TestContext.Current.CancellationToken);
        }

        Assert.Equal(new FormIdTextRunPlan(path, expectedSize), plan);
    }

    /// <summary>
    ///     Verifies that an empty file is planned as present with zero bytes, not as absent.
    /// </summary>
    [Fact]
    public async Task PlanAsync_EmptyFile_ReportsZeroBytesRatherThanAbsence()
    {
        var path = await WriteFormIdTextFileAsync("empty.txt", []);
        var sut = new FormIdTextIngestion();

        var plan = await sut.PlanAsync(CreateRequest(path, dryRun: true), TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextRunPlan(path, 0L), plan);
    }

    /// <summary>
    ///     Verifies that a missing file is planned as absent rather than failing the plan.
    /// </summary>
    /// <remarks>
    ///     Telling the user the file is missing is the point of looking it up, so absence is a reportable fact and not
    ///     a failure the plan raises.
    /// </remarks>
    [Fact]
    public async Task PlanAsync_MissingFile_ReportsAbsenceWithoutASize()
    {
        var path = Path.Combine(_testFilesDirectory, "missing.txt");
        var sut = new FormIdTextIngestion();

        var plan = await sut.PlanAsync(CreateRequest(path, dryRun: true), TestContext.Current.CancellationToken);

        Assert.Equal(new FormIdTextRunPlan(path, null), plan);
    }

    /// <summary>
    ///     Creates a FormID text-file request for the supplied path.
    /// </summary>
    /// <param name="path">The FormID text file the request names.</param>
    /// <param name="updateMode">The Update Mode the request carries to the writer.</param>
    /// <param name="dryRun">Whether the request is a Dry Run.</param>
    /// <returns>The request.</returns>
    private static FormIdTextProcessingRunRequest CreateRequest(
        string path,
        UpdateMode updateMode = UpdateMode.Append,
        bool dryRun = false)
    {
        return new FormIdTextProcessingRunRequest(
            path,
            @"C:\Databases\formids.db",
            GameRelease.SkyrimSE,
            updateMode,
            dryRun);
    }

    /// <summary>
    ///     Writes a FormID text file of the supplied lines into this test's isolated directory.
    /// </summary>
    /// <param name="fileName">The file name within the isolated directory.</param>
    /// <param name="lines">The lines to write, each followed by the platform newline.</param>
    /// <returns>The absolute path to the written file.</returns>
    private async Task<string> WriteFormIdTextFileAsync(string fileName, IEnumerable<string> lines)
    {
        var path = Path.Combine(_testFilesDirectory, fileName);
        await File.WriteAllLinesAsync(path, lines, TestContext.Current.CancellationToken);
        return path;
    }

    /// <summary>
    ///     Writes a FormID text file of fixed-width rows, split evenly between two Plugins.
    /// </summary>
    /// <param name="fileName">The file name within the isolated directory.</param>
    /// <param name="recordCount">The number of rows to write.</param>
    /// <param name="rowByteLength">The exact byte length of every row, including its CRLF terminator.</param>
    /// <returns>The absolute path to the written file.</returns>
    /// <remarks>
    ///     The rows carry an explicit CRLF rather than going through <c>File.WriteAllLines</c>, so the file is exactly
    ///     <paramref name="recordCount" /> × <paramref name="rowByteLength" /> bytes on any platform rather than on
    ///     whichever one supplies <see cref="Environment.NewLine" />. Every character is ASCII, so one character is
    ///     one UTF-8 byte, and the encoding is written without a byte-order mark for the same reason.
    /// </remarks>
    private async Task<string> WriteFixedWidthFormIdTextFileAsync(
        string fileName,
        int recordCount,
        int rowByteLength)
    {
        var contents = new StringBuilder(recordCount * rowByteLength);
        for (var index = 0; index < recordCount; index++)
        {
            var pluginName = index < recordCount / 2 ? "PluginA.esp" : "PluginB.esp";
            var row = $"{pluginName}|{index:X8}|Entry{index:D4}";
            Assert.Equal(rowByteLength - 2, row.Length);
            contents.Append(row).Append("\r\n");
        }

        var path = Path.Combine(_testFilesDirectory, fileName);
        await File.WriteAllTextAsync(
            path,
            contents.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            TestContext.Current.CancellationToken);

        return path;
    }
}
