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
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
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
    ///     Executes a real selected-Plugin Processing Run across aggregate Plugin Ingestion and the SQLite Store, proving
    ///     terminal completion is emitted only after explicit Store optimization has produced observable statistics.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SelectedPluginRun_IngestsThroughProductionSeamsAndCompletesAfterOptimization()
    {
        const string pluginName = "SelectedPlugin.esp";
        var gameDirectory = Path.Combine(_testDirectory, "Skyrim");
        var dataPath = GameReleaseHelper.ResolveDataPath(gameDirectory);
        Directory.CreateDirectory(dataPath);
        var pluginPath = Path.Combine(dataPath, pluginName);
        var databasePath = Path.Combine(_testDirectory, "selected-plugin.db");
        var sourcePlugin = new SkyrimMod(ModKey.FromNameAndExtension(pluginName), SkyrimRelease.SkyrimSE);
        var sourceRecord = sourcePlugin.Npcs.AddNew("NPC_Selected");
        sourceRecord.Name = "Selected NPC";
        sourcePlugin.WriteToBinary(pluginPath);

        var pluginIngestion = new PluginIngestion(new FixedGameLoadOrderProvider([pluginName]));
        using var executor = new ProcessingRunExecutor(
            pluginIngestion,
            new FormIdRecordStoreSessionOpener());
        var events = new List<ProcessingRunEvent>();
        var terminalObservedAfterOptimization = false;
        var progress = new SynchronousProgress<ProcessingRunEvent>(runEvent =>
        {
            events.Add(runEvent);
            if (runEvent is
                {
                    Kind: ProcessingRunEventKind.Status,
                    Value: 100
                } && runEvent.Message.Contains("Processing completed", StringComparison.Ordinal))
            {
                terminalObservedAfterOptimization = HasStoreStatistics(databasePath, GameRelease.SkyrimSE);
            }
        });

        await executor.ExecuteAsync(
            new PluginProcessingRunRequest(
                gameDirectory,
                databasePath,
                GameRelease.SkyrimSE,
                [pluginName],
                UpdateMode.Append),
            progress);

        Assert.True(terminalObservedAfterOptimization);

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
                    new FormIdStoredRecord(
                        pluginName,
                        sourceRecord.FormKey.ID.ToString("X6"),
                        "NPC_Selected")
                ],
                storedRecords);
        }

        var terminalEvent = events[^1];
        Assert.Equal(ProcessingRunEventKind.Status, terminalEvent.Kind);
        Assert.Contains("Processing completed successfully", terminalEvent.Message, StringComparison.Ordinal);
        Assert.Equal(100, terminalEvent.Value);
    }

    /// <summary>
    ///     Inspects whether explicit optimization has populated SQLite statistics for the selected GameRelease table.
    /// </summary>
    /// <param name="databasePath">The Processing Run database path.</param>
    /// <param name="gameRelease">The selected GameRelease table.</param>
    /// <returns><see langword="true" /> when optimization statistics are already observable.</returns>
    private static bool HasStoreStatistics(string databasePath, GameRelease gameRelease)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_stat1 WHERE tbl = @tableName";
        command.Parameters.AddWithValue("@tableName", gameRelease.ToString());
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private sealed class FixedGameLoadOrderProvider(IReadOnlyList<string> pluginNames) : IGameLoadOrderProvider
    {
        /// <inheritdoc />
        public GameLoadOrderSnapshot BuildSnapshot(
            GameRelease gameRelease,
            string dataPath,
            bool includeMasterFlagsLookup = false)
        {
            return new GameLoadOrderSnapshot(pluginNames);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetListedPluginNames(GameRelease gameRelease, string dataPath)
        {
            return pluginNames;
        }
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
