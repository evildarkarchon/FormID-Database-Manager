using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using Moq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace FormID_Database_Manager.Tests.Performance;

[SimpleJob(RunStrategy.ColdStart, 1, 1, 3)]
[MemoryDiagnoser]
public class PluginProcessingBenchmarks : IDisposable
{
    private SQLiteConnection _connection = null!;
    private string _databasePath = null!;
    private DatabaseService _databaseService = null!;
    private ModProcessor _modProcessor = null!;
    private string _testDirectory = null!;
    private List<PluginListItem> _testPlugins = null!;

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
        _databaseService = new DatabaseService();
        _modProcessor = new ModProcessor(_databaseService, _ => { });

        // Initialize database
        _databaseService.InitializeDatabase(_databasePath, GameRelease.SkyrimSE).Wait();

        // Create test plugins
        _testPlugins = CreateTestPlugins(PluginCount);

        // Setup connection
        _connection = new SQLiteConnection($"Data Source={_databasePath};Version=3;");
        _connection.Open();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _connection?.Dispose();

        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch { }
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
        var loadOrder = CreateLoadOrder(new[] { plugin });

        await _modProcessor.ProcessPlugin(
            _testDirectory,
            _connection,
            GameRelease.SkyrimSE,
            plugin,
            loadOrder,
            false,
            CancellationToken.None);
    }

    [Benchmark]
    public async Task ProcessMultiplePlugins_Sequential()
    {
        foreach (var plugin in _testPlugins)
        {
            var loadOrder = CreateLoadOrder(_testPlugins);

            await _modProcessor.ProcessPlugin(
                _testDirectory,
                _connection,
                GameRelease.SkyrimSE,
                plugin,
                loadOrder,
                false,
                CancellationToken.None);
        }
    }

    [Benchmark]
    public async Task ProcessPlugin_WithCancellation()
    {
        if (_testPlugins.Count == 0)
        {
            return;
        }

        var plugin = _testPlugins[0];
        var loadOrder = CreateLoadOrder(new[] { plugin });
        var cts = new CancellationTokenSource();

        // Cancel after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            cts.Cancel();
        });

        try
        {
            await _modProcessor.ProcessPlugin(
                _testDirectory,
                _connection,
                GameRelease.SkyrimSE,
                plugin,
                loadOrder,
                false,
                cts.Token);
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
        var loadOrder = CreateLoadOrder(new[] { plugin });

        // First insert some data
        await _modProcessor.ProcessPlugin(
            _testDirectory,
            _connection,
            GameRelease.SkyrimSE,
            plugin,
            loadOrder,
            false,
            CancellationToken.None);

        // Then update
        await _modProcessor.ProcessPlugin(
            _testDirectory,
            _connection,
            GameRelease.SkyrimSE,
            plugin,
            loadOrder,
            true,
            CancellationToken.None);
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

        // Extract entries (this would normally be done internally by ProcessPlugin)
        var entries = new List<(string formid, string entry)>();
        foreach (var record in mod.EnumerateMajorRecords())
        {
            if (record is IMajorRecordGetter majorRecord)
            {
                var formId = majorRecord.FormKey.ToString();
                var editorId = majorRecord.EditorID ?? "";
                var name = GetRecordName(majorRecord);
                var entry = string.IsNullOrEmpty(name) ? editorId : $"{editorId} - {name}";

                entries.Add((formId, entry));
            }
        }
    }

    private List<PluginListItem> CreateTestPlugins(int count)
    {
        var plugins = new List<PluginListItem>();

        for (var i = 0; i < count; i++)
        {
            var pluginName = $"TestPlugin_{i:D3}.esp";
            var pluginPath = Path.Combine(_testDirectory, pluginName);

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

            plugins.Add(new PluginListItem { Name = pluginName, IsSelected = true });
        }

        return plugins;
    }

    private IList<IModListingGetter<IModGetter>> CreateLoadOrder(IEnumerable<PluginListItem> plugins)
    {
        var loadOrder = new List<IModListingGetter<IModGetter>>();

        foreach (var plugin in plugins)
        {
            var modKey = ModKey.FromNameAndExtension(plugin.Name);
            // Create a simple mock listing
            var mockListing = new Mock<IModListingGetter<IModGetter>>();
            mockListing.Setup(x => x.ModKey).Returns(modKey);
            mockListing.Setup(x => x.Enabled).Returns(true);
            mockListing.Setup(x => x.ExistsOnDisk).Returns(true);

            loadOrder.Add(mockListing.Object);
        }

        return loadOrder;
    }

    private string GetRecordName(IMajorRecordGetter record)
    {
        if (!string.IsNullOrEmpty(record.EditorID))
        {
            return record.EditorID;
        }

        var namedRecord = record.GetType().GetInterfaces()
            .FirstOrDefault(i => i.Name.Contains("INamedGetter"));
        if (namedRecord != null)
        {
            var nameProperty = namedRecord.GetProperty("Name");
            var nameValue = nameProperty?.GetValue(record);
            if (nameValue != null)
            {
                var stringProperty = nameValue.GetType().GetProperty("String");
                var stringValue = stringProperty?.GetValue(nameValue) as string;
                if (!string.IsNullOrEmpty(stringValue))
                {
                    return stringValue;
                }
            }
        }

        return $"[{record.GetType().Name}_{record.FormKey.ID:X6}]";
    }
}
