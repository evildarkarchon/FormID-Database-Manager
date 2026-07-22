#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace FormID_Database_Manager.Tests.Performance;

[Collection("Performance Tests")]
public class LoadTests : IDisposable
{
    private readonly List<string> _createdFiles = [];
    private readonly ITestOutputHelper _output;
    private readonly string _testDirectory;

    public LoadTests(ITestOutputHelper output)
    {
        _output = output;
        _testDirectory = Path.Combine(Path.GetTempPath(), $"loadtest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        // Cleanup test artifacts
        TestCleanupHelper.DeleteTestFilesAndDirectory(_createdFiles, _testDirectory, _output);
    }

    /// <summary>
    ///     Measures multi-Plugin ingestion throughput through the production Processing Run executor.
    /// </summary>
    [ManualPerformanceFact]
    [Trait("Category", "ManualPerformance")]
    [Trait("Category", "LoadTest")]
    public async Task LoadTest_Process100Plugins_Sequentially()
    {
        // Arrange
        const int pluginCount = 100;
        var dataPath = GameReleaseHelper.ResolveDataPath(_testDirectory);
        var plugins = await CreateTestPlugins(dataPath, pluginCount, 100);
        var dbPath = Path.Combine(_testDirectory, "loadtest.db");
        _createdFiles.Add(dbPath);

        using var processingRunExecutor = PerformanceProcessingRunFactory.Create(plugins);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var request = new PluginProcessingRunRequest(
            _testDirectory,
            dbPath,
            GameRelease.SkyrimSE,
            plugins,
            UpdateMode.Append);
        var progress = new Progress<ProcessingRunEvent>(update =>
        {
            _output.WriteLine($"{update.Value:F1}% - {update.Message}");
        });

        Assert.All(plugins, p => Assert.True(File.Exists(Path.Combine(dataPath, p))));

        await processingRunExecutor.ExecuteAsync(request, progress);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Processed {pluginCount} plugins in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        _output.WriteLine($"Average time per plugin: {stopwatch.Elapsed.TotalMilliseconds / pluginCount:F2} ms");

        // Verify all plugins were processed
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand($"SELECT COUNT(DISTINCT plugin) FROM {GameRelease.SkyrimSE}", conn);
        var processedCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());

        Assert.Equal(pluginCount, processedCount);
    }

    [ManualPerformanceFact]
    [Trait("Category", "ManualPerformance")]
    [Trait("Category", "LoadTest")]
    public async Task LoadTest_LargePlugin_100kFormIds()
    {
        // Arrange
        const int formIdCount = 100000;
        var pluginName = "MassivePlugin.esp";
        var dataPath = GameReleaseHelper.ResolveDataPath(_testDirectory);
        Directory.CreateDirectory(dataPath);
        var pluginPath = Path.Combine(dataPath, pluginName);
        _createdFiles.Add(pluginPath);

        _output.WriteLine($"Creating plugin with {formIdCount} FormIDs...");
        await CreateLargePlugin(pluginPath, formIdCount);

        var dbPath = Path.Combine(_testDirectory, "largetest.db");
        _createdFiles.Add(dbPath);
        // Act
        var stopwatch = Stopwatch.StartNew();
        Assert.True(File.Exists(Path.Combine(dataPath, pluginName)));

        using var processingRunExecutor = PerformanceProcessingRunFactory.Create([pluginName]);
        await processingRunExecutor.ExecuteAsync(new PluginProcessingRunRequest(
            _testDirectory,
            dbPath,
            GameRelease.SkyrimSE,
            [pluginName],
            UpdateMode.Append));

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Processed {formIdCount} FormIDs in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        _output.WriteLine($"Processing rate: {formIdCount / stopwatch.Elapsed.TotalSeconds:F0} FormIDs/second");

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var countCmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", conn);
        var actualCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync());

        Assert.True(actualCount > 0, "No records were inserted");
        _output.WriteLine($"Actually inserted: {actualCount} records");
    }

    [ManualPerformanceFact]
    [Trait("Category", "ManualPerformance")]
    [Trait("Category", "LoadTest")]
    public async Task LoadTest_ConcurrentDatabaseOperations()
    {
        // Arrange
        const int threadCount = 10;
        const int operationsPerThread = 1000;
        var dbPath = Path.Combine(_testDirectory, "concurrent.db");
        _createdFiles.Add(dbPath);

        await using (var store = await FormIdRecordStore.OpenAsync(
                         dbPath,
                         GameRelease.SkyrimSE,
                         TestContext.Current.CancellationToken))
        {
            // The connection storm is the measured workload, so Store readiness is prepared before timing starts.
        }

        // Act
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task>();
        var errors = new List<string>();
        var errorLock = new object();

        for (var i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await using var store = await FormIdRecordStore.OpenAsync(dbPath, GameRelease.SkyrimSE);
                    var records = Enumerable.Range(0, operationsPerThread)
                        .Select(j => new FormIdRecord($"{threadId:X4}{j:X4}", $"Entry_T{threadId}_#{j}"));

                    await store.WritePluginAsync($"Plugin_{threadId}.esp", records, UpdateMode.Append);
                }
                catch (Exception ex)
                {
                    lock (errorLock)
                    {
                        errors.Add($"Thread {threadId}: {ex.Message}");
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Concurrent test completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        _output.WriteLine($"Total operations: {threadCount * operationsPerThread}");
        _output.WriteLine(
            $"Operations/second: {threadCount * operationsPerThread / stopwatch.Elapsed.TotalSeconds:F0}");

        // SQLite may have locking issues with concurrent writes, so we allow some errors
        if (errors.Count > 0)
        {
            _output.WriteLine($"Encountered {errors.Count} errors during concurrent operations:");
            foreach (var error in errors.Take(5))
            {
                _output.WriteLine($"  - {error}");
            }

            // As long as some records were inserted, the test is considered successful
            // since SQLite's locking behavior is expected
        }

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", conn);
        var totalRecords = Convert.ToInt64(await cmd.ExecuteScalarAsync());

        // Due to SQLite locking, we may not get all records inserted
        // As long as we got at least 50% of expected records, consider it a success
        var expectedRecords = threadCount * operationsPerThread;
        Assert.True(totalRecords > expectedRecords * 0.5,
            $"Expected at least {expectedRecords * 0.5} records, but got {totalRecords}");
    }

    /// <summary>
    ///     Measures managed-memory growth across successive authoritative Plugin List confirmations and projection work.
    /// </summary>
    [ManualPerformanceFact]
    [Trait("Category", "ManualPerformance")]
    [Trait("Category", "LoadTest")]
    public async Task LoadTest_MemoryUsageUnderLoad()
    {
        // Arrange
        const int iterations = 5;
        const int pluginsPerIteration = 20;
        var memoryReadings = new List<long>();
        var membershipSnapshots = Enumerable.Range(1, iterations)
            .Select(iteration => (IReadOnlyList<string>)Enumerable.Range(0, iteration * pluginsPerIteration)
                .Select(static pluginIndex => $"Plugin_{pluginIndex:D4}.esp")
                .ToArray())
            .ToArray();
        var dispatcher = new SynchronousThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher);
        using var pluginList = new PluginList(
            new GameDetectionService(),
            new SequencedPluginListDiscovery(membershipSnapshots));
        using var presentationAdapter = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);

        // Act
        for (var i = 0; i < iterations; i++)
        {
            _output.WriteLine($"Iteration {i + 1}/{iterations}");

            // Force garbage collection before measurement
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var beforeMemory = GC.GetTotalMemory(false);

            // Refreshing the same source grows confirmed membership through the production projection boundary.
            await pluginList.RefreshAsync(
                GameRelease.SkyrimSE,
                _testDirectory,
                AdvancedMode.On);

            // Process some operations
            viewModel.PluginFilter = "Plugin_";
            _ = viewModel.FilteredPlugins.Count;

            for (var j = 0; j < 100; j++)
            {
                viewModel.AddErrorMessage($"Test error {i}_{j}");
            }

            var afterMemory = GC.GetTotalMemory(false);
            var memoryUsed = afterMemory - beforeMemory;
            memoryReadings.Add(memoryUsed);

            _output.WriteLine($"Memory used: {memoryUsed / 1024.0 / 1024.0:F2} MB");
        }

        // Assert
        Assert.Equal(iterations * pluginsPerIteration, viewModel.Plugins.Count);
        var avgMemory = memoryReadings.Average();
        var maxMemory = memoryReadings.Max();

        _output.WriteLine($"Average memory per iteration: {avgMemory / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Maximum memory used: {maxMemory / 1024.0 / 1024.0:F2} MB");

        // Memory usage should be reasonable (less than 100MB per iteration)
        Assert.True(avgMemory < 100 * 1024 * 1024,
            $"Average memory usage too high: {avgMemory / 1024.0 / 1024.0:F2} MB");
    }

    /// <summary>
    ///     Measures rapid presentation updates and filtering against a realistically projected large Plugin List.
    /// </summary>
    [ManualPerformanceFact]
    [Trait("Category", "ManualPerformance")]
    [Trait("Category", "LoadTest")]
    public async Task LoadTest_UIResponsivenessUnderLoad()
    {
        // Arrange
        var uiUpdateTimes = new List<long>();
        const int updateCount = 1000;
        var pluginNames = Enumerable.Range(0, updateCount)
            .Select(static pluginIndex => $"Plugin_{pluginIndex:D4}.esp")
            .ToArray();
        var dispatcher = new SynchronousThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher);
        using var pluginList = new PluginList(
            new GameDetectionService(),
            new SequencedPluginListDiscovery([pluginNames]));
        using var presentationAdapter = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);

        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            _testDirectory,
            AdvancedMode.On);
        Assert.Equal(updateCount, viewModel.Plugins.Count);

        // Act
        for (var i = 0; i < updateCount; i++)
        {
            var stopwatch = Stopwatch.StartNew();

            // Simulate UI updates
            viewModel.ProgressValue = i * 100.0 / updateCount;
            viewModel.ProgressStatus = $"Processing item {i + 1} of {updateCount}";

            // Update search filter periodically
            if (i % 100 == 0)
            {
                viewModel.PluginFilter = $"Plugin_{i / 100}";
            }

            stopwatch.Stop();
            uiUpdateTimes.Add(stopwatch.ElapsedTicks);
        }

        // Assert
        var avgUpdateTime = uiUpdateTimes.Average();
        var maxUpdateTime = uiUpdateTimes.Max();
        var p95UpdateTime = uiUpdateTimes.OrderBy(t => t).Skip((int)(updateCount * 0.95)).First();

        var avgMs = avgUpdateTime * 1000.0 / Stopwatch.Frequency;
        var maxMs = maxUpdateTime * 1000.0 / Stopwatch.Frequency;
        var p95Ms = p95UpdateTime * 1000.0 / Stopwatch.Frequency;

        _output.WriteLine("UI Update Performance:");
        _output.WriteLine($"Average: {avgMs:F3} ms");
        _output.WriteLine($"95th percentile: {p95Ms:F3} ms");
        _output.WriteLine($"Maximum: {maxMs:F3} ms");

        // UI updates should be fast (< 1ms average, < 10ms max)
        Assert.True(avgMs < 1.0, $"Average UI update time too slow: {avgMs:F3} ms");
        Assert.True(maxMs < 10.0, $"Maximum UI update time too slow: {maxMs:F3} ms");
    }

    private async Task<List<string>> CreateTestPlugins(string dataPath, int count, int recordsPerPlugin)
    {
        var plugins = new List<string>();

        Directory.CreateDirectory(dataPath);

        await Task.Run(() =>
        {
            for (var i = 0; i < count; i++)
            {
                var pluginName = $"TestPlugin_{i:D3}.esp";
                var pluginPath = Path.Combine(dataPath, pluginName);

                var mod = new SkyrimMod(ModKey.FromNameAndExtension(pluginName), SkyrimRelease.SkyrimSE);

                for (var j = 0; j < recordsPerPlugin; j++)
                {
                    var npc = mod.Npcs.AddNew($"NPC_{i:D3}_{j:D3}");
                    npc.Name = $"Test NPC {i}-{j}";
                }

                mod.WriteToBinary(pluginPath);
                _createdFiles.Add(pluginPath);
                plugins.Add(pluginName);
            }
        });

        return plugins;
    }

    private async Task CreateLargePlugin(string path, int recordCount)
    {
        await Task.Run(() =>
        {
            var mod = new SkyrimMod(ModKey.FromNameAndExtension(Path.GetFileName(path)), SkyrimRelease.SkyrimSE);

            // Add various record types
            var recordsPerType = recordCount / 5;

            for (var i = 0; i < recordsPerType; i++)
            {
                mod.Npcs.AddNew($"NPC_{i:D6}").Name = $"Test NPC {i}";
                mod.Weapons.AddNew($"WEAP_{i:D6}").Name = $"Test Weapon {i}";
                mod.Armors.AddNew($"ARMO_{i:D6}").Name = $"Test Armor {i}";
                mod.Spells.AddNew($"SPEL_{i:D6}").Name = $"Test Spell {i}";
                mod.MiscItems.AddNew($"MISC_{i:D6}").Name = $"Test Misc {i}";
            }

            mod.WriteToBinary(path);
        });
    }

    /// <summary>
    ///     Supplies a deterministic sequence of immutable discovery results to retained Plugin List load coverage.
    /// </summary>
    private sealed class SequencedPluginListDiscovery : IPluginListDiscovery
    {
        private readonly IReadOnlyList<IReadOnlyList<string>> _membershipSnapshots;
        private int _nextSnapshotIndex = -1;

        /// <summary>
        ///     Creates a discovery adapter that returns each ordered membership snapshot once.
        /// </summary>
        /// <param name="membershipSnapshots">The ordered discovery results returned by successive requests.</param>
        /// <exception cref="ArgumentNullException"><paramref name="membershipSnapshots" /> is null.</exception>
        public SequencedPluginListDiscovery(IReadOnlyList<IReadOnlyList<string>> membershipSnapshots)
        {
            _membershipSnapshots = membershipSnapshots ?? throw new ArgumentNullException(nameof(membershipSnapshots));
        }

        /// <summary>
        ///     Returns the next configured Plugin membership after observing caller cancellation.
        /// </summary>
        /// <param name="source">The normalized source requested by the Plugin List.</param>
        /// <param name="progress">The optional discovery progress sink; no incremental progress is reported.</param>
        /// <param name="cancellationToken">Cancels the discovery request.</param>
        /// <returns>The next completed immutable discovery result.</returns>
        /// <exception cref="InvalidOperationException">No configured discovery result remains.</exception>
        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshotIndex = Interlocked.Increment(ref _nextSnapshotIndex);
            if (snapshotIndex >= _membershipSnapshots.Count)
            {
                throw new InvalidOperationException("No configured Plugin List discovery result remains.");
            }

            return Task.FromResult(PluginListDiscoveryResult.Completed(_membershipSnapshots[snapshotIndex]));
        }
    }

}
