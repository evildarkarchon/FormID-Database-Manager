#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Microsoft.Data.Sqlite;
using Moq;
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

        var executor = new ProcessingRunExecutor();
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);
        var request = new FormIdTextProcessingRunRequest(
            formIdTextFilePath,
            databasePath,
            GameRelease.SkyrimSE,
            UpdateMode.Append);

        var outcome = await executor.ExecuteAsync(request, progress, TestContext.Current.CancellationToken);

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

        // An appending run of fewer than a thousand rows reports only the opening and the finished import: no counter
        // update falls due, and append mode names no Plugin.
        Assert.Equal(
            [
                new ImportingFormIdText(0, 0, new FileInfo(formIdTextFilePath).Length, null),
                new ImportedFormIdText(new FormIdTextFileImportResult(2, 3))
            ],
            reports);

        // The run's own terminal fact is the returned outcome, carrying the counts the real FormID Text Ingestion read
        // for the rows the real Store committed.
        Assert.Equal(
            new FormIdTextFileImportResult(2, 3),
            Assert.IsType<FormIdTextRunOutcome>(outcome).ImportResult);
    }

    /// <summary>
    ///     Runs a real FormID text import through the User Workflow with no chosen database path and a default database
    ///     root that does not exist yet, proving the defaulted path is written back and the FormID Record Store creates
    ///     the folder it needs when the run opens it.
    /// </summary>
    /// <remarks>
    ///     Computing the default path creates nothing, so this is the end-to-end guard that a first real run on a fresh
    ///     profile still works: only the Store's own ready-on-return opening makes the directory (ADR-0001).
    /// </remarks>
    [Fact]
    public async Task ProcessFormIdsAsync_RealRunWithoutDatabasePath_WritesDefaultBackAndStoreCreatesItsFolder()
    {
        var defaultDatabaseRoot = Path.Combine(_testDirectory, "LocalAppData");
        var formIdTextFilePath = Path.Combine(_testDirectory, "formids.txt");
        await File.WriteAllLinesAsync(
            formIdTextFilePath,
            ["PluginA.esp|000001|First Entry"],
            TestContext.Current.CancellationToken);

        var dispatcher = new SynchronousThreadDispatcher();
        var viewModel = new MainWindowViewModel(dispatcher)
        {
            FormIdListPath = formIdTextFilePath
        };
        // No installed directories are declared, so selecting the game never asks the Plugin List to discover.
        using var sut = new UserWorkflow(
            viewModel,
            dispatcher,
            new Mock<IFileDialogService>(MockBehavior.Strict).Object,
            new GameInstallations(new InMemoryGameInstallationProbe()),
            new PluginList(new NoDiscoveryGameLoadOrders()),
            new ProcessingRunExecutor(),
            defaultDatabaseRoot);
        await sut.SelectGameReleaseAsync(GameRelease.SkyrimSE);
        Assert.False(Directory.Exists(defaultDatabaseRoot));

        await sut.ProcessFormIdsAsync();

        var expectedDatabasePath = Path.Combine(
            defaultDatabaseRoot,
            "FormID Database Manager",
            "Databases",
            "SkyrimSE.db");
        Assert.Empty(viewModel.ErrorMessages);
        Assert.Equal(expectedDatabasePath, viewModel.DatabasePath);
        Assert.True(File.Exists(expectedDatabasePath), $"Expected the run to create {expectedDatabasePath}.");

        await using var store = await FormIdRecordStore.OpenAsync(
            expectedDatabasePath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);
        var storedRecords = await store.ReadRecordsAsync(
            FormIdRecordQuery.All,
            TestContext.Current.CancellationToken);
        Assert.Equal([new FormIdStoredRecord("PluginA.esp", "000001", "First Entry")], storedRecords);
    }

    /// <summary>
    ///     Executes a real selected-Plugin Processing Run across aggregate Plugin Ingestion and the SQLite Store,
    ///     proving the run's outcome is returned only after explicit Store optimization has produced observable
    ///     statistics.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SelectedPluginRun_IngestsThroughProductionSeamsAndCompletesAfterOptimization()
    {
        const string pluginName = "SelectedPlugin.esp";
        var gameDirectory = Path.Combine(_testDirectory, "Skyrim");
        var dataPath = GameInstallations.CanonicalizeDataDirectory(gameDirectory);
        Directory.CreateDirectory(dataPath);
        var pluginPath = Path.Combine(dataPath, pluginName);
        var databasePath = Path.Combine(_testDirectory, "selected-plugin.db");
        var sourcePlugin = new SkyrimMod(ModKey.FromNameAndExtension(pluginName), SkyrimRelease.SkyrimSE);
        var sourceRecord = sourcePlugin.Npcs.AddNew("NPC_Selected");
        sourceRecord.Name = "Selected NPC";
        sourcePlugin.WriteToBinary(pluginPath);

        var gameLoadOrders = new GameLoadOrders(new FixtureGameLoadOrderEnvironment([pluginName]));
        var pluginIngestion = new PluginIngestion(gameLoadOrders);
        var executor = new ProcessingRunExecutor(
            pluginIngestion,
            new FormIdTextIngestion(),
            new FormIdRecordStoreSessionOpener());
        var reports = new List<ProcessingRunProgress>();
        var progress = new SynchronousProgress<ProcessingRunProgress>(reports.Add);

        var outcome = await executor.ExecuteAsync(
            new PluginProcessingRunRequest(
                PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
                databasePath,
                [pluginName],
                UpdateMode.Append),
            progress,
            TestContext.Current.CancellationToken);

        // The run withholds its outcome until explicit optimization has succeeded, so statistics are already
        // observable by the time the caller has anything to report.
        Assert.True(HasStoreStatistics(databasePath, GameRelease.SkyrimSE));

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

        var ingested = Assert.IsType<IngestedPlugin>(
            Assert.Single(Assert.IsType<PluginRunOutcome>(outcome).Report.Outcomes));
        Assert.Equal(pluginName, ingested.PluginName);
        Assert.Equal(1, ingested.FormIdCount);
        Assert.Null(ingested.Warning);
        // A run that succeeded reported only what it was doing: no failure, and no completed text import either.
        Assert.All(
            reports,
            reported => Assert.True(reported is PreparingLoadOrder or IngestingPlugin));
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

    /// <summary>
    ///     Game Load Orders for a run that must never discover Plugins; discovery fails the test loudly.
    /// </summary>
    private sealed class NoDiscoveryGameLoadOrders : PluginListGameLoadOrdersStub
    {
        /// <inheritdoc />
        public override Task<AvailablePluginsDiscoveryResult> DiscoverAvailablePluginsAsync(
            GameRelease gameRelease,
            string canonicalDataDirectory,
            IProgress<GameLoadOrderDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This run declares no installed directory, so nothing may discover.");
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
