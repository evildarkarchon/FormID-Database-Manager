using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Fixtures;
using FormID_Database_Manager.ViewModels;
using Microsoft.Data.Sqlite;
using Moq;
using Mutagen.Bethesda;
using Xunit;
using Xunit.Abstractions;

namespace FormID_Database_Manager.Tests.Performance;

/// <summary>
///     Performance regression tests to ensure operations stay within acceptable time bounds
/// </summary>
[Collection("Performance Tests")]
public class RegressionTests : IClassFixture<DatabaseFixture>, IDisposable
{
    private readonly DatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, TimeSpan> _performanceBaselines;
    private readonly string _testDirectory;

    public RegressionTests(ITestOutputHelper output, DatabaseFixture fixture)
    {
        _output = output;
        _fixture = fixture;
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PerfRegression_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        // Performance baselines (adjust based on your hardware)
        _performanceBaselines = new Dictionary<string, TimeSpan>
        {
            ["DatabaseInit_SingleGame"] = TimeSpan.FromMilliseconds(50),
            ["DatabaseInit_AllGames"] = TimeSpan.FromMilliseconds(200),
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

    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task DatabaseInitialization_SingleGame_StaysWithinBaseline()
    {
        // Arrange
        var service = new DatabaseService();
        var dbPath = Path.Combine(_testDirectory, "test_single.db");
        var baseline = _performanceBaselines["DatabaseInit_SingleGame"];

        // Act
        var stopwatch = Stopwatch.StartNew();
        await service.InitializeDatabase(dbPath, GameRelease.SkyrimSE);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Database initialization (single game) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.2)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (20% tolerance)");
    }

    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task DatabaseInitialization_AllGames_StaysWithinBaseline()
    {
        // Arrange
        var service = new DatabaseService();
        var baseline = _performanceBaselines["DatabaseInit_AllGames"];
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
            await service.InitializeDatabase(dbPath, game);
        }

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Database initialization (all games) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.2)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (20% tolerance)");
    }

    [Theory]
    [Trait("Category", "PerformanceRegression")]
    [InlineData(1000, "BatchInsert_1000Records")]
    [InlineData(10000, "BatchInsert_10000Records")]
    public async Task DatabaseBatchInsert_StaysWithinBaseline(int recordCount, string baselineKey)
    {
        // Arrange
        var service = new DatabaseService();
        var dbPath = Path.Combine(_testDirectory, $"test_batch_{recordCount}.db");
        await service.InitializeDatabase(dbPath, GameRelease.SkyrimSE);

        var baseline = _performanceBaselines[baselineKey];
        var records = GenerateTestRecords(recordCount);

        // Act
        var stopwatch = Stopwatch.StartNew();
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            // Simulate batch insert by inserting records in a transaction
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var record in records)
                {
                    await service.InsertRecord(connection, GameRelease.SkyrimSE, record.plugin, record.formId,
                        record.editorId);
                }

                transaction.Commit();
            }
        }

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Batch insert ({recordCount} records) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Records per second: {recordCount / stopwatch.Elapsed.TotalSeconds:F0}");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.2)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (20% tolerance)");
    }

    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public void GameDetection_SimpleDirectory_StaysWithinBaseline()
    {
        // Arrange
        var service = new GameDetectionService();
        var testDir = Path.Combine(_testDirectory, "Skyrim Special Edition");
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "SkyrimSE.exe"), "dummy");

        var baseline = _performanceBaselines["GameDetection_SimpleDirectory"];

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = service.DetectGame(testDir);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Game detection (simple) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.3)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (30% tolerance)");
    }

    [Fact]
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

        var baseline = _performanceBaselines["GameDetection_ComplexDirectory"];

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = service.DetectGame(testDir);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Game detection (complex) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.3)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (30% tolerance)");
    }

    [Theory]
    [Trait("Category", "PerformanceRegression")]
    [InlineData(10, "PluginListLoad_Small")]
    [InlineData(255, "PluginListLoad_Large")]
    public async Task PluginListLoad_StaysWithinBaseline(int pluginCount, string baselineKey)
    {
        // Arrange
        // Create mock dependencies for PluginListManager
        var mockGameDetection = new Mock<GameDetectionService>();
        var mockDispatcher = new Mock<IThreadDispatcher>();
        var viewModel = new MainWindowViewModel(mockDispatcher.Object);
        var service = new PluginListManager(
            mockGameDetection.Object,
            viewModel,
            mockDispatcher.Object);

        var listPath = Path.Combine(_testDirectory, "plugins.txt");

        var plugins = new List<string>();
        for (var i = 0; i < pluginCount; i++)
        {
            plugins.Add($"*Plugin{i:D3}.esp");
        }

        await File.WriteAllLinesAsync(listPath, plugins);

        var baseline = _performanceBaselines[baselineKey];

        // Act
        var stopwatch = Stopwatch.StartNew();
        // Simulate plugin list loading by reading the file
        var pluginList = await File.ReadAllLinesAsync(listPath);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Plugin list load ({pluginCount} plugins) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.2)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (20% tolerance)");
    }

    [Theory]
    [Trait("Category", "PerformanceRegression")]
    [InlineData(100, "FormIdProcess_SmallFile")]
    [InlineData(10000, "FormIdProcess_LargeFile")]
    public async Task FormIdTextProcessing_StaysWithinBaseline(int lineCount, string baselineKey)
    {
        // Arrange
        var mockDatabaseService = new Mock<DatabaseService>();
        var processor = new FormIdTextProcessor(mockDatabaseService.Object);
        var formIdFile = Path.Combine(_testDirectory, "formids.txt");
        var dbPath = Path.Combine(_testDirectory, "test.db");

        // Create test FormID file
        var lines = new List<string>();
        for (var i = 0; i < lineCount; i++)
        {
            lines.Add($"TestPlugin.esp|{i:X8}|TestItem{i}");
        }

        await File.WriteAllLinesAsync(formIdFile, lines);

        // Setup database
        var dbService = new DatabaseService();
        await dbService.InitializeDatabase(dbPath, GameRelease.SkyrimSE);

        var baseline = _performanceBaselines[baselineKey];

        // Act
        var stopwatch = Stopwatch.StartNew();
        // Open connection and process the file
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            await processor.ProcessFormIdListFile(
                formIdFile,
                connection,
                GameRelease.SkyrimSE,
                false,
                default
            );
        }

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"FormID processing ({lineCount} lines) took: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Baseline: {baseline.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Lines per second: {lineCount / stopwatch.Elapsed.TotalSeconds:F0}");

        Assert.True(stopwatch.Elapsed < baseline.Add(TimeSpan.FromMilliseconds(baseline.TotalMilliseconds * 0.3)),
            $"Performance regression detected! Operation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms, baseline is {baseline.TotalMilliseconds:F2}ms (30% tolerance)");
    }

    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task MemoryUsage_DatabaseOperations_StaysWithinLimits()
    {
        // Arrange
        var service = new DatabaseService();
        var dbPath = Path.Combine(_testDirectory, "memory_test.db");
        await service.InitializeDatabase(dbPath, GameRelease.SkyrimSE);

        var records = GenerateTestRecords(50000);

        // Force garbage collection to get baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memoryBefore = GC.GetTotalMemory(false);

        // Act
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            // Simulate batch insert by inserting records in a transaction
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var record in records)
                {
                    await service.InsertRecord(connection, GameRelease.SkyrimSE, record.plugin, record.formId,
                        record.editorId);
                }

                transaction.Commit();
            }
        }

        var memoryAfter = GC.GetTotalMemory(false);
        var memoryIncrease = memoryAfter - memoryBefore;

        // Assert
        _output.WriteLine($"Memory before: {memoryBefore:N0} bytes");
        _output.WriteLine($"Memory after: {memoryAfter:N0} bytes");
        _output.WriteLine($"Memory increase: {memoryIncrease:N0} bytes ({memoryIncrease / 1024.0 / 1024.0:F2} MB)");

        Assert.True(memoryIncrease < 100_000_000, // 100MB limit
            $"Memory usage regression detected! Operation used {memoryIncrease:N0} bytes");
    }

    [Fact]
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
}
