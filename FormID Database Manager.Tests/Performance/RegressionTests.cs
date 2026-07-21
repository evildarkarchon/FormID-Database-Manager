using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Performance;

/// <summary>
///     Performance regression tests to ensure operations stay within acceptable time bounds
/// </summary>
[Collection("Performance Tests")]
[Trait("Category", "ManualPerformance")]
public class RegressionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, TimeSpan> _performanceBaselines;
    private readonly string _testDirectory;

    public RegressionTests(ITestOutputHelper output)
    {
        _output = output;
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PerfRegression_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        // Performance baselines (adjust based on your hardware)
        _performanceBaselines = new Dictionary<string, TimeSpan>
        {
            ["StoreOpen_SingleGame"] = TimeSpan.FromMilliseconds(50),
            ["StoreOpen_AllGames"] = TimeSpan.FromMilliseconds(200),
            ["BatchInsert_1000Records"] = TimeSpan.FromMilliseconds(100),
            ["BatchInsert_10000Records"] = TimeSpan.FromSeconds(1),
            ["GameDetection_SimpleDirectory"] = TimeSpan.FromMilliseconds(10),
            ["GameDetection_ComplexDirectory"] = TimeSpan.FromMilliseconds(50),
            ["PluginListLoad_Small"] = TimeSpan.FromMilliseconds(20),
            ["PluginListLoad_Large"] = TimeSpan.FromMilliseconds(100),
            ["FormIdProcess_SmallFile"] = TimeSpan.FromMilliseconds(50),
            ["FormIdProcess_LargeFile"] = TimeSpan.FromMilliseconds(500)
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    ///     Guards the full ready-on-return Store-opening contract for one GameRelease.
    /// </summary>
    [ManualPerformanceFact]
    [Trait("Category", "PerformanceRegression")]
    public async Task FormIdRecordStoreOpen_SingleGame_StaysWithinBaseline()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "test_single.db");
        var baseline = _performanceBaselines["StoreOpen_SingleGame"];

        // Act
        var stopwatch = Stopwatch.StartNew();
        await using (var store = await FormIdRecordStore.OpenAsync(
                         dbPath,
                         GameRelease.SkyrimSE,
                         TestContext.Current.CancellationToken))
        {
            // Disposal completes inside the timer so the baseline covers the full Store open-and-close lifecycle.
        }
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Store opening (single game) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.2)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (20% tolerance)");
    }

    /// <summary>
    ///     Guards the aggregate ready-on-return Store-opening contract across supported GameReleases.
    /// </summary>
    [ManualPerformanceFact]
    [Trait("Category", "PerformanceRegression")]
    public async Task FormIdRecordStoreOpen_AllGames_StaysWithinBaseline()
    {
        // Arrange
        var baseline = _performanceBaselines["StoreOpen_AllGames"];
        var games = new[]
        {
            GameRelease.SkyrimSE, GameRelease.Fallout4, GameRelease.Starfield, GameRelease.SkyrimVR,
            GameRelease.Oblivion
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        foreach (var game in games)
        {
            var dbPath = Path.Combine(_testDirectory, $"test_{game}.db");
            await using (var store = await FormIdRecordStore.OpenAsync(
                             dbPath,
                             game,
                             TestContext.Current.CancellationToken))
            {
                // Each disposal remains inside the timer so every GameRelease measures the same complete lifecycle.
            }
        }

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Store opening (all games) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.2)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (20% tolerance)");
    }

    [ManualPerformanceTheory]
    [Trait("Category", "PerformanceRegression")]
    [InlineData(1000, "BatchInsert_1000Records")]
    [InlineData(10000, "BatchInsert_10000Records")]
    public async Task DatabaseBatchInsert_StaysWithinBaseline(int recordCount, string baselineKey)
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, $"test_batch_{recordCount}.db");

        var baseline = _performanceBaselines[baselineKey];
        var records = GenerateTestRecords(recordCount);

        // Act
        var stopwatch = Stopwatch.StartNew();
        await WriteRecordsWithStoreAsync(dbPath, records);

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Batch insert ({recordCount} records) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Records per second: {recordCount / stopwatch.Elapsed.TotalSeconds:F0}");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.2)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (20% tolerance)");
    }

    [ManualPerformanceFact]
    [Trait("Category", "PerformanceRegression")]
    public void GameDetection_SimpleDirectory_StaysWithinBaseline()
    {
        // Arrange
        var service = new GameDetectionService();
        var testDir = Path.Combine(_testDirectory, "Skyrim Special Edition");
        var dataDir = Path.Combine(testDir, "Data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(testDir, "SkyrimSE.exe"), "dummy");
        File.WriteAllText(Path.Combine(dataDir, "Skyrim.esm"), "dummy");

        var baseline = _performanceBaselines["GameDetection_SimpleDirectory"];

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = service.DetectGame(testDir);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Game detection (simple) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");

        Assert.Equal(GameRelease.SkyrimSE, result);
        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.3)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (30% tolerance)");
    }

    [ManualPerformanceFact]
    [Trait("Category", "PerformanceRegression")]
    public void GameDetection_ComplexDirectory_StaysWithinBaseline()
    {
        // Arrange
        var service = new GameDetectionService();
        var testDir = Path.Combine(_testDirectory, "ComplexGame");
        var dataDir = Path.Combine(testDir, "Data");
        Directory.CreateDirectory(dataDir);

        // Create many files to simulate complex directory
        for (var i = 0; i < 100; i++)
        {
            File.WriteAllText(Path.Combine(dataDir, $"file{i}.esm"), "dummy");
        }

        File.WriteAllText(Path.Combine(dataDir, "Fallout4.esm"), "dummy");

        var baseline = _performanceBaselines["GameDetection_ComplexDirectory"];

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = service.DetectGame(testDir);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Game detection (complex) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");

        Assert.Equal(GameRelease.Fallout4, result);
        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.3)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (30% tolerance)");
    }

    [ManualPerformanceTheory]
    [Trait("Category", "PerformanceRegression")]
    [InlineData(10, "PluginListLoad_Small")]
    [InlineData(255, "PluginListLoad_Large")]
    public async Task PluginListLoad_StaysWithinBaseline(int pluginCount, string baselineKey)
    {
        // Arrange
        var listPath = Path.Combine(_testDirectory, "plugins.txt");

        var plugins = new List<string>();
        for (var i = 0; i < pluginCount; i++)
        {
            plugins.Add($"*Plugin{i:D3}.esp");
        }

        await File.WriteAllLinesAsync(listPath, plugins, TestContext.Current.CancellationToken);

        var baseline = _performanceBaselines[baselineKey];

        // Act
        var stopwatch = Stopwatch.StartNew();
        // Simulate plugin list loading by reading the file
        var pluginList = await File.ReadAllLinesAsync(listPath, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Plugin list load ({pluginCount} plugins) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");

        Assert.Equal(pluginCount, pluginList.Length);
        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.2)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (20% tolerance)");
    }

    [ManualPerformanceTheory]
    [Trait("Category", "PerformanceRegression")]
    [InlineData(100, "FormIdProcess_SmallFile")]
    [InlineData(10000, "FormIdProcess_LargeFile")]
    public async Task FormIdTextProcessing_StaysWithinBaseline(int lineCount, string baselineKey)
    {
        // Arrange
        var formIdFile = Path.Combine(_testDirectory, "formids.txt");
        var dbPath = Path.Combine(_testDirectory, "test.db");

        // Create test FormID file
        var lines = new List<string>();
        for (var i = 0; i < lineCount; i++)
        {
            lines.Add($"TestPlugin.esp|{i:X8}|TestItem{i}");
        }

        await File.WriteAllLinesAsync(formIdFile, lines, TestContext.Current.CancellationToken);

        await using (var store = await FormIdRecordStore.OpenAsync(
                         dbPath,
                         GameRelease.SkyrimSE,
                         TestContext.Current.CancellationToken))
        {
            // Keep the import baseline prewarmed while preparing it through the production Store seam.
        }

        var baseline = _performanceBaselines[baselineKey];

        // Act
        var stopwatch = Stopwatch.StartNew();
        await using (var recordStore = await FormIdRecordStore.OpenAsync(
                         dbPath,
                         GameRelease.SkyrimSE,
                         TestContext.Current.CancellationToken))
        {
            await recordStore.ImportFormIdTextFileAsync(
                formIdFile,
                UpdateMode.Append,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"FormID processing ({lineCount} lines) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Lines per second: {lineCount / stopwatch.Elapsed.TotalSeconds:F0}");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.3)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (30% tolerance)");
    }

    [ManualPerformanceFact]
    [Trait("Category", "PerformanceRegression")]
    public async Task MemoryUsage_DatabaseOperations_StaysWithinLimits()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "memory_test.db");

        var records = GenerateTestRecords(50000);

        // Force garbage collection to get baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memoryBefore = GC.GetTotalMemory(false);

        // Act
        await WriteRecordsWithStoreAsync(dbPath, records);

        var memoryAfter = GC.GetTotalMemory(false);
        var memoryIncrease = memoryAfter - memoryBefore;

        // Assert
        _output.WriteLine($"Memory before: {memoryBefore:N0} bytes");
        _output.WriteLine($"Memory after: {memoryAfter:N0} bytes");
        _output.WriteLine($"Memory increase: {memoryIncrease:N0} bytes ({memoryIncrease / 1024.0 / 1024.0:F2} MB)");

        Assert.True(memoryIncrease < 100_000_000, // 100MB limit
            $"Memory usage regression detected! Operation used {memoryIncrease:N0} bytes");
    }

    [ManualPerformanceFact]
    [Trait("Category", "PerformanceRegression")]
    public void CpuUsage_IntensiveOperations_StaysReasonable()
    {
        // Arrange
        var service = new GameDetectionService();
        var testDir = Path.Combine(_testDirectory, "CPU_Test");
        var dataDir = Path.Combine(testDir, "Data");
        Directory.CreateDirectory(dataDir);

        // Create many files
        for (var i = 0; i < 1000; i++)
        {
            File.WriteAllText(Path.Combine(dataDir, $"file{i}.esm"), "dummy");
        }

        var processBefore = Process.GetCurrentProcess();
        var cpuBefore = processBefore.TotalProcessorTime;

        // Act
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 5000; i++)
        {
            service.DetectGame(testDir);
        }

        stopwatch.Stop();

        var processAfter = Process.GetCurrentProcess();
        var cpuAfter = processAfter.TotalProcessorTime;
        var cpuUsed = cpuAfter - cpuBefore;
        var cpuPercentage = cpuUsed.TotalMilliseconds / stopwatch.Elapsed.TotalMilliseconds * 100;

        // Assert
        _output.WriteLine($"CPU time used: {cpuUsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Wall time: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"CPU usage: {cpuPercentage:F2}%");

        Assert.True(cpuPercentage < 1000, // Allow for multi-threading (up to 10 cores/variance)
            $"CPU usage regression detected! Operation used {cpuPercentage:F2}% CPU");
    }

    private List<(string plugin, string formId, string editorId)> GenerateTestRecords(int count)
    {
        var records = new List<(string, string, string)>();
        var random = new Random(42); // Fixed seed for consistency

        for (var i = 0; i < count; i++)
        {
            records.Add((
                $"TestPlugin{random.Next(10)}.esp",
                $"{random.Next(0xFFFFFF):X6}",
                $"TestItem_{i}"
            ));
        }

        return records;
    }

    private static async Task WriteRecordsWithStoreAsync(
        string dbPath,
        IReadOnlyList<(string plugin, string formId, string editorId)> records)
    {
        await using var store = await FormIdRecordStore.OpenAsync(
            dbPath,
            GameRelease.SkyrimSE,
            TestContext.Current.CancellationToken);

        foreach (var pluginGroup in records.GroupBy(static record => record.plugin))
        {
            var pluginRecords = pluginGroup.Select(static record => new FormIdRecord(record.formId, record.editorId));
            await store.WritePluginAsync(
                pluginGroup.Key,
                pluginRecords,
                UpdateMode.Append,
                TestContext.Current.CancellationToken);
        }
    }
}
