using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Performance;

[Config(typeof(MemoryConfig))]
[SimpleJob(RunStrategy.ColdStart, 1, 0, 3)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class MemoryBenchmarks
{
    private string _testDirectory = null!;

    [Params(1000, 10000, 50000)] public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"membench_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine(
                    $"Failed to delete temporary benchmark directory '{_testDirectory}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine(
                    $"Failed to delete temporary benchmark directory '{_testDirectory}': {ex.Message}");
            }
        }
    }

    [Benchmark(Baseline = true)]
    public int PluginCollection_Memory()
    {
        var plugins = new ObservableCollection<PluginListItem>();

        for (var i = 0; i < ItemCount; i++)
        {
            plugins.Add(new PluginListItem
            {
                Name = $"Plugin_{i:D6}.esp", IsSelected = i % 2 == 0
                // LoadIndex not available
            });
        }

        // Force collection to ensure memory is allocated
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return plugins.Count;
    }

    [Benchmark]
    public void ViewModel_WithLargePluginList()
    {
        var viewModel = new MainWindowViewModel();

        for (var i = 0; i < ItemCount; i++)
        {
            viewModel.Plugins.Add(new PluginListItem
            {
                Name = $"Plugin_{i:D6}.esp", IsSelected = i % 2 == 0
                // LoadIndex not available
            });
        }

        // Apply filter to test filtered collection memory
        viewModel.PluginFilter = "Plugin_1";

        // Access filtered plugins to ensure they're created
        _ = viewModel.FilteredPlugins.Count;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Benchmark]
    public int ErrorMessages_LargeCollection()
    {
        var viewModel = new MainWindowViewModel();

        for (var i = 0; i < ItemCount / 10; i++) // Fewer error messages typically
        {
            viewModel.AddErrorMessage(
                $"Error {i}: This is a test error message with some details about what went wrong.");
        }

        // Access error text to ensure it's generated
        // Access error messages collection instead

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return viewModel.ErrorMessages.Count;
    }

    [Benchmark]
    public async Task FormIdRecordStore_LargeTransaction()
    {
        var dbPath = Path.Combine(_testDirectory, "memory_test.db");

        var entries = new List<FormIdRecord>();
        for (var i = 0; i < ItemCount; i++)
        {
            entries.Add(new FormIdRecord($"{i:X8}", $"Entry_{i}"));
        }

        await using var store = await FormIdRecordStore.OpenAsync(dbPath, GameRelease.SkyrimSE);
        await store.WritePluginAsync("Plugin.esp", entries, UpdateMode.Append);

        // Clean up
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }

    /// <summary>
    ///     Measures allocations for a large in-memory collection shaped like FormID text rows.
    /// </summary>
    [Benchmark]
    public void FormIdTextRows_LargeInMemoryCollection()
    {
        // Keep database I/O out of this benchmark so it isolates the row collection's managed-memory cost.
        var testData = new List<string>(ItemCount);
        for (var i = 0; i < ItemCount; i++)
        {
            testData.Add($"Plugin.esp|{i:X8}|Entry_{i}");
        }

        _ = testData.Count;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Cleanup not needed
    }

    [Benchmark]
    public int ProcessingParameters_LargePluginList()
    {
        var parameters = new ProcessingParameters
        {
            GameDirectory = @"C:\Games\Skyrim",
            DatabasePath = @"C:\Games\test.db",
            GameRelease = GameRelease.SkyrimSE,
            SelectedPlugins = [],
            UpdateMode = false
        };

        // Add many selected plugins
        for (var i = 0; i < ItemCount; i++)
        {
            parameters.SelectedPlugins.Add(new PluginListItem
            {
                Name = $"Plugin_{i:D6}.esp", IsSelected = true
                // LoadIndex not available
            });
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return parameters.SelectedPlugins.Count;
    }

    [Benchmark]
    public int StringConcatenation_FormIdEntries()
    {
        var entries = new List<string>(ItemCount);

        for (var i = 0; i < ItemCount; i++)
        {
            var formId = $"{i:X8}";
            var editorId = $"ITEM_{i:D6}";
            var name = $"Test Item {i}";

            // Simulate the string concatenation done during processing
            var entry = string.IsNullOrEmpty(name) ? editorId : $"{editorId} - {name}";
            entries.Add($"{formId}|{entry}");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return entries.Count;
    }

    [Benchmark]
    public async Task<long> FileDialog_MultipleSelections()
    {
        var tasks = new List<Task<long>>();

        // Simulate memory usage of multiple dialog operations
        for (var i = 0; i < 10; i++)
        {
            var dialogIndex = i;
            tasks.Add(Task.Run(() =>
            {
                var before = GC.GetTotalMemory(false);

                // Simulate dialog data
                var dialogData = new Dictionary<string, object>
                {
                    ["Title"] = $"Dialog {dialogIndex}",
                    ["Path"] = Path.Combine(_testDirectory, $"file_{dialogIndex}.txt"),
                    ["Filters"] = new[] { "*.txt", "*.esp", "*.esm", "*.esl" },
                    ["Items"] = Enumerable.Range(0, ItemCount / 10).Select(j => $"Item_{j}").ToList()
                };

                GC.KeepAlive(dialogData);
                var after = GC.GetTotalMemory(false);
                return after - before;
            }));
        }

        var memoryDeltas = await Task.WhenAll(tasks);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return memoryDeltas.Sum();
    }

    public class MemoryConfig : ManualConfig
    {
        public MemoryConfig()
        {
            AddColumn(StatisticColumn.Mean);
            AddColumn(StatisticColumn.StdDev);
            AddColumn(BaselineRatioColumn.RatioMean);
            AddDiagnoser(MemoryDiagnoser.Default);
        }
    }
}
