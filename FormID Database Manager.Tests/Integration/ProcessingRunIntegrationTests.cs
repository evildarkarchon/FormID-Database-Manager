#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Integration;

/// <summary>
///     Integration coverage for Processing Runs that cross the real SQLite FormID Record Store boundary.
/// </summary>
[Collection("Integration Tests")]
public sealed class ProcessingRunIntegrationTests : IDisposable
{
    private readonly string _testDirectory;

    /// <summary>
    ///     Creates an isolated directory owned by this integration-test instance.
    /// </summary>
    public ProcessingRunIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"processing_run_integration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    /// <summary>
    ///     Executes a FormID text-file Processing Run through a real SQLite store and verifies the complete run path.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FormIdTextFileRun_OpensImportsOptimizesStoresRecordsAndCompletes()
    {
        var databasePath = Path.Combine(_testDirectory, "formids.db");
        var formIdTextFilePath = Path.Combine(_testDirectory, "formids.txt");
        await File.WriteAllLinesAsync(
            formIdTextFilePath,
            [
                "PluginA.esp|000001|First Entry",
                "PluginA.esp|000002|Second Entry",
                "PluginB.esm|000003|Third Entry"
            ],
            TestContext.Current.CancellationToken);

        using var executor = new ProcessingRunExecutor();
        var events = new List<ProcessingRunEvent>();
        var progress = new SynchronousProgress<ProcessingRunEvent>(events.Add);
        var request = new FormIdTextProcessingRunRequest(
            formIdTextFilePath,
            databasePath,
            GameRelease.SkyrimSE,
            UpdateMode.Append);

        await executor.ExecuteAsync(request, progress);

        Assert.True(File.Exists(databasePath));

        var inspectionConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        await using (var connection = new SqliteConnection(inspectionConnectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var statisticsCommand = connection.CreateCommand();
            statisticsCommand.CommandText = "SELECT COUNT(*) FROM sqlite_stat1 WHERE tbl = @tableName";
            statisticsCommand.Parameters.AddWithValue("@tableName", GameRelease.SkyrimSE.ToString());

            // A new store's indexes lack statistics, so the real PRAGMA optimize populates sqlite_stat1.
            var statisticsCount = Convert.ToInt32(
                await statisticsCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken));
            Assert.True(statisticsCount > 0);
        }

        await using (var store = await FormIdRecordStore.OpenAsync(
                         databasePath,
                         GameRelease.SkyrimSE,
                         TestContext.Current.CancellationToken))
        {
            var storedRecords = await store.ReadRecordsAsync(
                FormIdRecordQuery.All,
                TestContext.Current.CancellationToken);

            Assert.Equal(
                [
                    new FormIdStoredRecord("PluginA.esp", "000001", "First Entry"),
                    new FormIdStoredRecord("PluginA.esp", "000002", "Second Entry"),
                    new FormIdStoredRecord("PluginB.esm", "000003", "Third Entry")
                ],
                storedRecords
                    .OrderBy(record => record.Plugin, StringComparer.Ordinal)
                    .ThenBy(record => record.FormId, StringComparer.Ordinal));
        }

        Assert.Contains(events, runEvent =>
            runEvent.Kind == ProcessingRunEventKind.Status &&
            runEvent.Message.Contains("Completed processing 2 plugins", StringComparison.Ordinal) &&
            runEvent.Value == 100);

        // The executor emits terminal success only after the real store's optimization completes.
        var terminalEvent = events[^1];
        Assert.Equal(ProcessingRunEventKind.Status, terminalEvent.Kind);
        Assert.Contains("Processing completed successfully", terminalEvent.Message, StringComparison.Ordinal);
        Assert.Equal(100, terminalEvent.Value);
    }

    /// <summary>
    ///     Releases pooled SQLite handles and removes the integration test directory.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_testDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
        catch (IOException exception)
        {
            Debug.WriteLine($"Failed to delete temporary directory '{_testDirectory}': {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine($"Failed to delete temporary directory '{_testDirectory}': {exception.Message}");
        }
    }
}
