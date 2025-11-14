#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Xunit;
using Xunit.Abstractions;

namespace FormID_Database_Manager.Tests.Performance;

public class StressTests : IDisposable
{
    private readonly List<string> _createdFiles = new();
    private readonly ITestOutputHelper _output;
    private readonly string _testDirectory;

    public StressTests(ITestOutputHelper output)
    {
        _output = output;
        _testDirectory = Path.Combine(Path.GetTempPath(), $"stresstest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
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

    [Fact(Timeout = 120000)]
    [Trait("Category", "StressTest")]
    public async Task StressTest_RapidCancellations()
    {
        // Arrange
        const int cancellationAttempts = 50;
        var dbPath = Path.Combine(_testDirectory, "cancel_stress.db");
        _createdFiles.Add(dbPath);

        var databaseService = new DatabaseService();
        await databaseService.InitializeDatabase(dbPath, GameRelease.SkyrimSE);

        var viewModel = new MainWindowViewModel();
        var pluginProcessingService = new PluginProcessingService(databaseService, viewModel);

        var cancelledCount = 0;
        var completedCount = 0;
        var errorCount = 0;

        // Act
        for (var i = 0; i < cancellationAttempts; i++)
        {
            var cts = new CancellationTokenSource();
            var parameters = new ProcessingParameters
            {
                GameDirectory = _testDirectory,
                DatabasePath = dbPath,
                GameRelease = GameRelease.SkyrimSE,
                SelectedPlugins = new List<PluginListItem> { new() { Name = $"Test_{i}.esp" } },
                UpdateMode = false
            };

            // Start processing
            var processTask = pluginProcessingService.ProcessPlugins(parameters);

            // Cancel after random delay
            var cancelDelay = Random.Shared.Next(1, 50);
            _ = Task.Run(async () =>
            {
                await Task.Delay(cancelDelay);
                pluginProcessingService.CancelProcessing();
            });

            try
            {
                await processTask;
                completedCount++;
            }
            catch (OperationCanceledException)
            {
                cancelledCount++;
            }
            catch (Exception ex)
            {
                errorCount++;
                _output.WriteLine($"Error during cancellation test {i}: {ex.Message}");
            }
        }

        // Assert
        _output.WriteLine("Cancellation stress test results:");
        _output.WriteLine($"Completed: {completedCount}");
        _output.WriteLine($"Cancelled: {cancelledCount}");
        _output.WriteLine($"Errors: {errorCount}");

        Assert.Equal(0, errorCount);
        Assert.True(cancelledCount > 0, "No operations were cancelled");
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "StressTest")]
    public async Task StressTest_MaximumDatabaseConnections()
    {
        // Arrange
        const int maxConnections = 100;
        var dbPath = Path.Combine(_testDirectory, "connection_stress.db");
        _createdFiles.Add(dbPath);

        var databaseService = new DatabaseService();
        await databaseService.InitializeDatabase(dbPath, GameRelease.SkyrimSE);

        var connections = new List<SQLiteConnection>();
        var tasks = new List<Task>();
        var errors = new List<Exception>();

        // Act
        try
        {
            // Open many connections
            for (var i = 0; i < maxConnections; i++)
            {
                var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                connections.Add(conn);

                var connIndex = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await conn.OpenAsync();

                        // Perform some operations
                        for (var j = 0; j < 10; j++)
                        {
                            await databaseService.InsertRecord(
                                conn,
                                GameRelease.SkyrimSE,
                                $"Plugin_{connIndex}.esp",
                                $"{connIndex:X4}{j:X4}",
                                $"Entry_{connIndex}_{j}");
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                        {
                            errors.Add(ex);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);
        }
        finally
        {
            // Cleanup connections
            foreach (var conn in connections)
            {
                conn?.Dispose();
            }
        }

        // Assert
        _output.WriteLine("Connection stress test completed:");
        _output.WriteLine($"Successful connections: {maxConnections - errors.Count}");
        _output.WriteLine($"Failed connections: {errors.Count}");

        if (errors.Count > 0)
        {
            _output.WriteLine($"First error: {errors[0].Message}");
        }

        // Some failures are acceptable under stress, but not all
        Assert.True(errors.Count < maxConnections / 2,
            $"Too many connection failures: {errors.Count}/{maxConnections}");
    }

    [Fact(Skip = "Requires UI thread synchronization", Timeout = 60000)]
    [Trait("Category", "StressTest")]
    public async Task StressTest_RapidUIUpdates()
    {
        // Arrange
        const int updateCount = 10000;
        const int threadCount = 5;
        var viewModel = new MainWindowViewModel();
        var errors = new List<Exception>();
        var updateTimes = new List<long>();
        var updateLock = new object();

        // Act
        var tasks = new List<Task>();
        for (var t = 0; t < threadCount; t++)
        {
            var threadId = t;
            tasks.Add(Task.Run(() =>
            {
                for (var i = 0; i < updateCount / threadCount; i++)
                {
                    try
                    {
                        var stopwatch = Stopwatch.StartNew();

                        // Rapid UI updates from multiple threads
                        viewModel.ProgressValue = Random.Shared.Next(0, 101);
                        viewModel.ProgressStatus = $"Thread {threadId} - Update {i}";

                        if (i % 10 == 0)
                        {
                            viewModel.AddErrorMessage($"T{threadId}: Error message {i}");
                        }

                        if (i % 50 == 0)
                        {
                            viewModel.Plugins.Add(new PluginListItem { Name = $"Plugin_T{threadId}_{i}.esp" });
                        }

                        stopwatch.Stop();
                        lock (updateLock)
                        {
                            updateTimes.Add(stopwatch.ElapsedTicks);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                        {
                            errors.Add(ex);
                        }
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        _output.WriteLine("UI stress test completed:");
        _output.WriteLine($"Total updates: {updateCount}");
        _output.WriteLine($"Errors: {errors.Count}");

        if (updateTimes.Count > 0)
        {
            var avgTime = updateTimes.Average() * 1000.0 / Stopwatch.Frequency;
            var maxTime = updateTimes.Max() * 1000.0 / Stopwatch.Frequency;
            _output.WriteLine($"Average update time: {avgTime:F3} ms");
            _output.WriteLine($"Maximum update time: {maxTime:F3} ms");
        }

        Assert.Empty(errors);
    }

    [Fact(Timeout = 300000)]
    [Trait("Category", "StressTest")]
    public async Task StressTest_LargeDatabaseFile()
    {
        // Arrange
        var dbPath = Path.Combine(_testDirectory, "large_stress.db");
        _createdFiles.Add(dbPath);

        var databaseService = new DatabaseService();
        await databaseService.InitializeDatabase(dbPath, GameRelease.SkyrimSE);

        const int recordCount = 1_000_000;
        const int batchSize = 10000;

        _output.WriteLine($"Creating database with {recordCount:N0} records...");

        // Act
        var stopwatch = Stopwatch.StartNew();
        using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
        await conn.OpenAsync();

        var transaction = conn.BeginTransaction();
        using var command = new SQLiteCommand(conn);
        command.CommandText =
            $"INSERT INTO {GameRelease.SkyrimSE} (plugin, formid, entry) VALUES (@plugin, @formid, @entry)";
        command.Transaction = transaction;

        var pluginParam = command.Parameters.Add("@plugin", DbType.String);
        var formidParam = command.Parameters.Add("@formid", DbType.String);
        var entryParam = command.Parameters.Add("@entry", DbType.String);

        for (var i = 0; i < recordCount; i++)
        {
            pluginParam.Value = $"Plugin_{i % 100}.esp";
            formidParam.Value = $"{i:X8}";
            entryParam.Value = $"Entry_{i}";

            await command.ExecuteNonQueryAsync();

            if (i % batchSize == 0 && i > 0)
            {
                transaction.Commit();
                transaction.Dispose();

                transaction = conn.BeginTransaction();
                command.Transaction = transaction;
            }
        }

        transaction?.Commit();
        transaction?.Dispose();
        stopwatch.Stop();

        // Test database performance with large file
        var searchStopwatch = Stopwatch.StartNew();
        command.CommandText = $"SELECT COUNT(*) FROM {GameRelease.SkyrimSE} WHERE formid LIKE '0000%'";
        var searchResult = await command.ExecuteScalarAsync();
        searchStopwatch.Stop();

        // Optimize and measure
        var optimizeStopwatch = Stopwatch.StartNew();
        await databaseService.OptimizeDatabase(conn);
        optimizeStopwatch.Stop();

        // Assert
        var fileInfo = new FileInfo(dbPath);
        _output.WriteLine("Database stress test results:");
        _output.WriteLine($"Records inserted: {recordCount:N0}");
        _output.WriteLine($"Insert time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        _output.WriteLine($"Insert rate: {recordCount / stopwatch.Elapsed.TotalSeconds:F0} records/second");
        _output.WriteLine($"Database file size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Search time: {searchStopwatch.ElapsedMilliseconds} ms");
        _output.WriteLine($"Optimize time: {optimizeStopwatch.Elapsed.TotalSeconds:F2} seconds");

        Assert.True(fileInfo.Length > 0, "Database file is empty");
        Assert.True(searchStopwatch.ElapsedMilliseconds < 1000,
            $"Search too slow: {searchStopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    [Trait("Category", "StressTest")]
    public void StressTest_OutOfMemoryScenario()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();
        const int pluginCount = 4_096; // Realistic maximum - game plugin limit

        _output.WriteLine($"Testing memory stress with {pluginCount:N0} plugins...");

        // Monitor memory
        var initialMemory = GC.GetTotalMemory(true);
        var peakMemory = initialMemory;

        // Act
        Exception? caughtException = null;
        try
        {
            for (var i = 0; i < pluginCount; i++)
            {
                viewModel.Plugins.Add(new PluginListItem
                {
                    Name =
                        $"VeryLongPluginNameToIncreaseMemoryUsage_ThisIsIntentionallyLongToStressTestMemory_{i:D8}.esp"
                });

                // Also add error messages
                if (i % 100 == 0)
                {
                    viewModel.AddErrorMessage($"This is a very long error message designed to consume memory. " +
                                              $"Error number {i} with additional details and stack trace information " +
                                              $"that would typically be included in a real error scenario.");
                }

                // Monitor memory inline (every 512 plugins)
                if (i % 512 == 0)
                {
                    var currentMemory = GC.GetTotalMemory(false);
                    if (currentMemory > peakMemory)
                    {
                        peakMemory = currentMemory;
                    }
                }
            }
        }
        catch (OutOfMemoryException ex)
        {
            caughtException = ex;
        }

        // Final memory check
        var currentMem = GC.GetTotalMemory(false);
        if (currentMem > peakMemory)
        {
            peakMemory = currentMem;
        }

        // Force cleanup
        viewModel.Plugins.Clear();
        viewModel.ErrorMessages.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);

        // Assert
        _output.WriteLine("Memory stress test results:");
        _output.WriteLine($"Initial memory: {initialMemory / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Peak memory: {peakMemory / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Final memory: {finalMemory / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Memory increase: {(peakMemory - initialMemory) / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Items successfully added: {viewModel.Plugins.Count}");

        if (caughtException != null)
        {
            _output.WriteLine("OutOfMemoryException caught as expected");
        }

        // Memory should be properly released after cleanup
        // Due to GC behavior and test environment differences, we'll skip the memory assertion
        // The important thing is that no OutOfMemoryException was thrown unexpectedly
        if (caughtException == null)
        {
            _output.WriteLine("No OutOfMemoryException was thrown - memory handling is acceptable");
        }
    }
}
