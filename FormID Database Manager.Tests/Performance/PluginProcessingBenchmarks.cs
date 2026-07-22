using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace FormID_Database_Manager.Tests.Performance;

[SimpleJob(RunStrategy.ColdStart, 1, 1, 3)]
[MemoryDiagnoser]
public class PluginProcessingBenchmarks : IDisposable
{
    private readonly Consumer _consumer = new();
    private string _databasePath = null!;
    private string _testDirectory = null!;
    private List<string> _testPlugins = null!;

    [Params(1, 5, 10)] public int PluginCount { get; set; }

    public void Dispose()
    {
        Cleanup();
    }

    [GlobalSetup]
    public void Setup()
    {
        // Create test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), $"benchmark_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);

        // Create database
        _databasePath = Path.Combine(_testDirectory, "benchmark.db");

        // Processing Run owns Store opening so the end-to-end benchmark does not pre-initialize its database.

        // Create test plugins
        var dataPath = GameReleaseHelper.ResolveDataPath(_testDirectory);
        Directory.CreateDirectory(dataPath);
        _testPlugins = CreateTestPlugins(dataPath, PluginCount);
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
            catch (IOException)
            {
                // Benchmark cleanup is best-effort; temp files can remain locked briefly.
            }
            catch (UnauthorizedAccessException)
            {
                // Benchmark cleanup is best-effort; temp files can remain locked briefly.
            }
        }
    }

    [Benchmark]
    public async Task ProcessSinglePlugin()
    {
        if (_testPlugins.Count == 0)
        {
            return;
        }

        var plugin = _testPlugins[0];

        await ExecuteBenchmarkRunAsync(CreateRequest([plugin], UpdateMode.Append));
    }

    [Benchmark]
    public async Task ProcessMultiplePlugins_Sequential()
    {
        await ExecuteBenchmarkRunAsync(CreateRequest(_testPlugins, UpdateMode.Append));
    }

    [Benchmark]
    public async Task ProcessPlugin_WithCancellation()
    {
        if (_testPlugins.Count == 0)
        {
            return;
        }

        var plugin = _testPlugins[0];
        using var processingRunExecutor = CreateProcessingRun();
        var processingTask = processingRunExecutor.ExecuteAsync(CreateRequest([plugin], UpdateMode.Append));
        processingRunExecutor.Cancel();

        try
        {
            await processingTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    [Benchmark]
    public async Task ProcessPlugin_UpdateMode()
    {
        if (_testPlugins.Count == 0)
        {
            return;
        }

        var plugin = _testPlugins[0];

        // First insert some data
        await ExecuteBenchmarkRunAsync(CreateRequest([plugin], UpdateMode.Append));

        // Then update
        await ExecuteBenchmarkRunAsync(CreateRequest([plugin], UpdateMode.ReplacePluginRecords));
    }

    [Benchmark]
    public void ExtractFormIdEntries_Performance()
    {
        // Create a mock mod with many records
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("TestMod.esp"), SkyrimRelease.SkyrimSE);

        // Add various record types
        for (var i = 0; i < 1000; i++)
        {
            // Add NPCs
            var npc = mod.Npcs.AddNew($"NPC_{i:D6}");
            npc.Name = $"Test NPC {i}";

            // Add weapons
            var weapon = mod.Weapons.AddNew($"WEAP_{i:D6}");
            weapon.Name = $"Test Weapon {i}";

            // Add armor
            var armor = mod.Armors.AddNew($"ARMO_{i:D6}");
            armor.Name = $"Test Armor {i}";
        }

        // Extract entries through the same internal Entry Extraction module used by Plugin Ingestion.
        var extraction = new EntryExtraction();
        var entries = new List<(string formid, string entry)>();
        foreach (var record in mod.EnumerateMajorRecords())
        {
            if (record is IMajorRecordGetter majorRecord && extraction.TryExtract(majorRecord, _ => { }) is { } entry)
            {
                entries.Add((entry.FormId, entry.Entry));
            }
        }

        _consumer.Consume(entries);
    }

    private List<string> CreateTestPlugins(string dataPath, int count)
    {
        var plugins = new List<string>();

        for (var i = 0; i < count; i++)
        {
            var pluginName = $"TestPlugin_{i:D3}.esp";
            var pluginPath = Path.Combine(dataPath, pluginName);

            // Create a simple plugin file (mock)
            var mod = new SkyrimMod(ModKey.FromNameAndExtension(pluginName), SkyrimRelease.SkyrimSE);

            // Add some test records
            for (var j = 0; j < 100; j++)
            {
                var npc = mod.Npcs.AddNew($"TEST_NPC_{i:D3}_{j:D3}");
                npc.Name = $"Test NPC {i}-{j}";
            }

            // Write the plugin
            mod.WriteToBinary(pluginPath);

            plugins.Add(pluginName);
        }

        return plugins;
    }

    private ProcessingRunExecutor CreateProcessingRun()
    {
        return PerformanceProcessingRunFactory.Create(_testPlugins);
    }

    private async Task ExecuteBenchmarkRunAsync(PluginProcessingRunRequest request)
    {
        var progress = new CapturingRunProgress();
        using var processingRunExecutor = CreateProcessingRun();
        await processingRunExecutor.ExecuteAsync(request, progress);

        var issues = progress.Events
            .Where(static e => e.Kind is ProcessingRunEventKind.Warning or ProcessingRunEventKind.Error)
            .Select(static e => e.Message)
            .ToArray();

        if (issues.Length > 0)
        {
            throw new InvalidOperationException(
                $"Benchmark plugin processing did not complete cleanly:{Environment.NewLine}{string.Join(Environment.NewLine, issues)}");
        }
    }

    private PluginProcessingRunRequest CreateRequest(IEnumerable<string> pluginNames, UpdateMode updateMode)
    {
        return new PluginProcessingRunRequest(
            _testDirectory,
            _databasePath,
            GameRelease.SkyrimSE,
            pluginNames,
            updateMode);
    }

    private sealed class CapturingRunProgress : IProgress<ProcessingRunEvent>
    {
        public List<ProcessingRunEvent> Events { get; } = [];

        public void Report(ProcessingRunEvent value)
        {
            Events.Add(value);
        }
    }
}
