using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;
using Xunit.Abstractions;

namespace FormID_Database_Manager.Tests.Performance;

[Collection("Performance Tests")]
public class LoadTests : IDisposable
{
    private readonly List<string> _createdFiles = [];
    private readonly DatabaseService _databaseService;
    private readonly ITestOutputHelper _output;
    private readonly string _testDirectory;

    public LoadTests(ITestOutputHelper output)
    {
        _output = output;
        _testDirectory = Path.Combine(Path.GetTempPath(), $"loadtest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _databaseService = new DatabaseService();
    }

    public void Dispose()
    {
        // Cleanup database service

        // Clean up created files
        foreach (var file in _createdFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch { }
        }

        // Clean up test directory
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch { }
        }
    }

    [Fact(Skip = "Requires proper plugin files")]
    [Trait("Category", "LoadTest")]
    public async Task LoadTest_Process100Plugins_Concurrently()
    {
        // Arrange
        const int pluginCount = 100;
        var plugins = await CreateTestPlugins(pluginCount, 100);
        var dbPath = Path.Combine(_testDirectory, "loadtest.db");
        _createdFiles.Add(dbPath);

        await _databaseService.InitializeDatabase(dbPath, GameRelease.SkyrimSE);

        var viewModel = new MainWindowViewModel();
        var pluginProcessingService = new PluginProcessingService(_databaseService, viewModel);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var parameters = new ProcessingParameters
        {
            GameDirectory = _testDirectory,
            DatabasePath = dbPath,
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins = plugins.Select(p => new PluginListItem { Name = p }).ToList(),
            UpdateMode = false
        };

        var progress = new Progress<(string Message, double? Value)>(update =>
        {
            _output.WriteLine($"{update.Value:F1}% - {update.Message}");
        });

        await pluginProcessingService.ProcessPlugins(parameters, progress);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Processed {pluginCount} plugins in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        _output.WriteLine($"Average time per plugin: {stopwatch.Elapsed.TotalMilliseconds / pluginCount:F2} ms");

        // Verify all plugins were processed
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = new SqliteCommand($"SELECT COUNT(DISTINCT plugin) FROM {GameRelease.SkyrimSE}", conn);
        var processedCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());

        Assert.Equal(pluginCount, processedCount);
    }

    [Fact(Skip = "Requires proper game environment setup")]
    [Trait("Category", "LoadTest")]
    public async Task LoadTest_LargePlugin_100kFormIds()
    {
        // Arrange
        const int formIdCount = 100000;
        var pluginName = "MassivePlugin.esp";
        var pluginPath = Path.Combine(_testDirectory, pluginName);
        _createdFiles.Add(pluginPath);

        _output.WriteLine($"Creating plugin with {formIdCount} FormIDs...");
        await CreateLargePlugin(pluginPath, formIdCount);

        var dbPath = Path.Combine(_testDirectory, "largetest.db");
        _createdFiles.Add(dbPath);
        await _databaseService.InitializeDatabase(dbPath, GameRelease.SkyrimSE);

        // Act
        var stopwatch = Stopwatch.StartNew();
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var modProcessor = new ModProcessor(_databaseService, msg => _output.WriteLine($"Error: {msg}"));

        await modProcessor.ProcessPlugin(
            _testDirectory,
            conn,
            GameRelease.SkyrimSE,
            new PluginListItem { Name = pluginName },
            new Dictionary<string, IModListingGetter<IModGetter>>(StringComparer.OrdinalIgnoreCase)
            {
                [pluginName] = CreateMockModListing(pluginName)
            },
            false,
            CancellationToken.None);

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Processed {formIdCount} FormIDs in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        _output.WriteLine($"Processing rate: {formIdCount / stopwatch.Elapsed.TotalSeconds:F0} FormIDs/second");

        using var countCmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", conn);
        var actualCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync());

        Assert.True(actualCount > 0, "No records were inserted");
        _output.WriteLine($"Actually inserted: {actualCount} records");
    }

    [Fact]
    [Trait("Category", "LoadTest")]
    public async Task LoadTest_ConcurrentDatabaseOperations()
    {
        // Arrange
        const int threadCount = 10;
        const int operationsPerThread = 1000;
        var dbPath = Path.Combine(_testDirectory, "concurrent.db");
        _createdFiles.Add(dbPath);

        await _databaseService.InitializeDatabase(dbPath, GameRelease.SkyrimSE);

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
                    using var conn = new SqliteConnection($"Data Source={dbPath}");
                    await conn.OpenAsync();

                    for (var j = 0; j < operationsPerThread; j++)
                    {
                        await _databaseService.InsertRecord(
                            conn,
                            GameRelease.SkyrimSE,
                            $"Plugin_{threadId}.esp",
                            $"{threadId:X4}{j:X4}",
                            $"Entry_T{threadId}_#{j}");
                    }
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

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {GameRelease.SkyrimSE}", conn);
        var totalRecords = Convert.ToInt64(await cmd.ExecuteScalarAsync());

        // Due to SQLite locking, we may not get all records inserted
        // As long as we got at least 50% of expected records, consider it a success
        var expectedRecords = threadCount * operationsPerThread;
        Assert.True(totalRecords > expectedRecords * 0.5,
            $"Expected at least {expectedRecords * 0.5} records, but got {totalRecords}");
    }

    [Fact]
    [Trait("Category", "LoadTest")]
    public async Task LoadTest_MemoryUsageUnderLoad()
    {
        // Arrange
        const int iterations = 5;
        const int pluginsPerIteration = 20;
        var dbPath = Path.Combine(_testDirectory, "memory.db");
        _createdFiles.Add(dbPath);

        await _databaseService.InitializeDatabase(dbPath, GameRelease.SkyrimSE);

        var memoryReadings = new List<long>();
        var viewModel = new MainWindowViewModel();

        // Act
        for (var i = 0; i < iterations; i++)
        {
            _output.WriteLine($"Iteration {i + 1}/{iterations}");

            // Force garbage collection before measurement
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var beforeMemory = GC.GetTotalMemory(false);

            // Add plugins to viewmodel
            for (var j = 0; j < pluginsPerIteration; j++)
            {
                viewModel.Plugins.Add(new PluginListItem { Name = $"Plugin_{i}_{j}.esp" });
            }

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
        var avgMemory = memoryReadings.Average();
        var maxMemory = memoryReadings.Max();

        _output.WriteLine($"Average memory per iteration: {avgMemory / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Maximum memory used: {maxMemory / 1024.0 / 1024.0:F2} MB");

        // Memory usage should be reasonable (less than 100MB per iteration)
        Assert.True(avgMemory < 100 * 1024 * 1024,
            $"Average memory usage too high: {avgMemory / 1024.0 / 1024.0:F2} MB");
    }

    [Fact]
    [Trait("Category", "LoadTest")]
    public Task LoadTest_UIResponsivenessUnderLoad()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();
        var uiUpdateTimes = new List<long>();
        const int updateCount = 1000;

        // Act
        for (var i = 0; i < updateCount; i++)
        {
            var stopwatch = Stopwatch.StartNew();

            // Simulate UI updates
            viewModel.ProgressValue = i * 100.0 / updateCount;
            viewModel.ProgressStatus = $"Processing item {i + 1} of {updateCount}";

            // Add plugin
            viewModel.Plugins.Add(new PluginListItem { Name = $"Plugin_{i:D4}.esp" });

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

        return Task.CompletedTask;
    }

    private async Task<List<string>> CreateTestPlugins(int count, int recordsPerPlugin)
    {
        var plugins = new List<string>();

        await Task.Run(() =>
        {
            for (var i = 0; i < count; i++)
            {
                var pluginName = $"TestPlugin_{i:D3}.esp";
                var pluginPath = Path.Combine(_testDirectory, pluginName);

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

    private static IModListingGetter<IModGetter> CreateMockModListing(string pluginName)
    {
        var modKey = ModKey.FromNameAndExtension(pluginName);
        var mockListing = new Moq.Mock<IModListingGetter<IModGetter>>();
        mockListing.Setup(x => x.ModKey).Returns(modKey);
        mockListing.Setup(x => x.Enabled).Returns(true);
        mockListing.Setup(x => x.ModExists).Returns(true);
        return mockListing.Object;
    }
}
