#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Microsoft.Data.Sqlite;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Tests.Performance;

[Config(typeof(MemoryConfig))]
[SimpleJob(RunStrategy.ColdStart, 1, 0, 3)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class MemoryBenchmarks
{
    private string _formIdTextDatabasePath = null!;
    private string _formIdTextFilePath = null!;
    private IReadOnlyList<string> _pluginNames = null!;
    private string _testDirectory = null!;

    [Params(1000, 10000, 50000)] public int ItemCount { get; set; }

    /// <summary>
    ///     Creates reusable Plugin names, FormID text input, and database paths for each benchmark parameter set.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"membench_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _formIdTextDatabasePath = Path.Combine(_testDirectory, "text_import.db");
        _formIdTextFilePath = Path.Combine(_testDirectory, "formids.txt");
        _pluginNames = Enumerable.Range(0, ItemCount)
            .Select(static i => $"Plugin_{i:D6}.esp")
            .ToArray();
        File.WriteAllLines(
            _formIdTextFilePath,
            Enumerable.Range(0, ItemCount).Select(static i => $"Plugin.esp|{i:X8}|Entry_{i}"));
    }

    /// <summary>
    ///     Releases SQLite file handles and removes the benchmark's temporary workspace.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        // Pooled SQLite connections can retain Windows file handles after each store is disposed.
        SqliteConnection.ClearAllPools();

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

    /// <summary>
    ///     Measures a large authoritative Plugin List confirmation projected into the Main Window ViewModel.
    /// </summary>
    /// <returns>The number of projected Plugins matching the benchmark filter.</returns>
    [Benchmark(Baseline = true)]
    public async Task<int> ViewModel_WithLargePluginList()
    {
        var dispatcher = new SynchronousThreadDispatcher();
        using var viewModel = new MainWindowViewModel(dispatcher);
        using var pluginList = new PluginList(
            new GameDetectionService(),
            new DeterministicPluginListDiscovery(_pluginNames));
        using var presentationAdapter = new PluginListPresentationAdapter(pluginList, viewModel, dispatcher);

        await pluginList.RefreshAsync(
            GameRelease.SkyrimSE,
            _testDirectory,
            AdvancedMode.On);

        // Apply filter to test filtered collection memory
        viewModel.PluginFilter = "Plugin_1";

        // Access filtered plugins to ensure they're created
        var filteredCount = viewModel.FilteredPlugins.Count;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return filteredCount;
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
        var dbPath = Path.Combine(_testDirectory, $"memory_test_{Guid.NewGuid():N}.db");

        var entries = new List<FormIdRecord>();
        for (var i = 0; i < ItemCount; i++)
        {
            entries.Add(new FormIdRecord($"{i:X8}", $"Entry_{i}"));
        }

        await using var store = await FormIdRecordStore.OpenAsync(dbPath, GameRelease.SkyrimSE);
        await store.WritePluginAsync("Plugin.esp", entries, UpdateMode.Append);
    }

    /// <summary>
    ///     Measures managed allocations while importing a FormID text file through the production record-store seam.
    /// </summary>
    [Benchmark]
    public async Task<FormIdTextFileImportResult> FormIdRecordStore_ImportTextFile()
    {
        await using var store = await FormIdRecordStore.OpenAsync(
            _formIdTextDatabasePath,
            GameRelease.SkyrimSE);
        return await store.ImportFormIdTextFileAsync(
            _formIdTextFilePath,
            UpdateMode.ReplacePluginRecords);
    }

    /// <summary>
    ///     Measures the immutable Plugin-name snapshot captured by a typed Processing Run request.
    /// </summary>
    [Benchmark]
    public int PluginProcessingRunRequest_LargePluginList()
    {
        var request = new PluginProcessingRunRequest(
            @"C:\Games\Skyrim",
            @"C:\Games\test.db",
            GameRelease.SkyrimSE,
            _pluginNames,
            UpdateMode.Append);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return request.PluginNames.Count;
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

    /// <summary>
    ///     Supplies one immutable, precomputed discovery result to the authoritative Plugin List benchmark.
    /// </summary>
    /// <param name="pluginNames">The ordered Plugin names returned for every discovery request.</param>
    private sealed class DeterministicPluginListDiscovery(IReadOnlyList<string> pluginNames) : IPluginListDiscovery
    {
        /// <summary>
        ///     Returns the configured ordered Plugin names after observing caller cancellation.
        /// </summary>
        /// <param name="source">The normalized source requested by the Plugin List.</param>
        /// <param name="progress">The optional discovery progress sink; no incremental progress is reported.</param>
        /// <param name="cancellationToken">Cancels the discovery request.</param>
        /// <returns>A completed immutable discovery result.</returns>
        public Task<PluginListDiscoveryResult> DiscoverAsync(
            PluginListSource source,
            IProgress<PluginListDiscoveryProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PluginListDiscoveryResult.Completed(pluginNames));
        }
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
