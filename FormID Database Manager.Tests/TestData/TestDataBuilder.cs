using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FormID_Database_Manager.Tests.TestData;

/// <summary>
/// Builder class for creating test data files and structures used across various tests.
/// </summary>
public static class TestDataBuilder
{
    private static readonly string TestDataRoot = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, 
        "TestData");

    /// <summary>
    /// Ensures all test data directories exist.
    /// </summary>
    public static void EnsureTestDataStructure()
    {
        // Plugin directories
        Directory.CreateDirectory(Path.Combine(TestDataRoot, "Plugins", "Skyrim"));
        Directory.CreateDirectory(Path.Combine(TestDataRoot, "Plugins", "Fallout4"));
        Directory.CreateDirectory(Path.Combine(TestDataRoot, "Plugins", "Starfield"));
        Directory.CreateDirectory(Path.Combine(TestDataRoot, "Plugins", "Oblivion"));

        // FormID list directories
        Directory.CreateDirectory(Path.Combine(TestDataRoot, "FormIdLists", "Valid"));
        Directory.CreateDirectory(Path.Combine(TestDataRoot, "FormIdLists", "Invalid"));
        Directory.CreateDirectory(Path.Combine(TestDataRoot, "FormIdLists", "EdgeCases"));

        // Plugin list directories
        Directory.CreateDirectory(Path.Combine(TestDataRoot, "PluginLists"));
    }

    /// <summary>
    /// Creates a minimal valid ESP/ESM plugin file.
    /// </summary>
    public static void CreateMinimalPlugin(string directory, string fileName, int formIdCount = 10)
    {
        var filePath = Path.Combine(directory, fileName);
        var isEsm = fileName.EndsWith(".esm", StringComparison.OrdinalIgnoreCase);
        
        using var fs = new FileStream(filePath, FileMode.Create);
        using var writer = new BinaryWriter(fs);

        // TES4 Header
        writer.Write(Encoding.ASCII.GetBytes("TES4")); // Record type
        writer.Write(0x2B); // Data size
        writer.Write(isEsm ? 0x00000001 : 0x00000000); // Flags (ESM flag if .esm)
        writer.Write(0x00000000); // FormID
        writer.Write(0x00000000); // Timestamp
        writer.Write(0x00000000); // Version control
        writer.Write(0x00000000); // Internal version
        
        // Add some basic subrecords
        writer.Write(Encoding.ASCII.GetBytes("HEDR")); // Header subrecord
        writer.Write((short)12); // Size
        writer.Write(1.0f); // Version
        writer.Write(formIdCount); // Number of records
        writer.Write(0x00000800); // Next available FormID

        // Add fake records for testing
        for (int i = 0; i < formIdCount; i++)
        {
            AddFakeRecord(writer, i + 1);
        }
    }

    private static void AddFakeRecord(BinaryWriter writer, int formId)
    {
        // WEAP record (weapon)
        writer.Write(Encoding.ASCII.GetBytes("WEAP"));
        writer.Write(50); // Data size
        writer.Write(0x00000000); // Flags
        writer.Write(formId); // FormID
        writer.Write(0x00000000); // Timestamp
        writer.Write(0x00000000); // Version control

        // EDID subrecord (Editor ID)
        writer.Write(Encoding.ASCII.GetBytes("EDID"));
        writer.Write((short)15);
        writer.Write(Encoding.ASCII.GetBytes($"TestWeapon{formId:D3}\0"));
    }

    /// <summary>
    /// Creates a valid FormID list file.
    /// </summary>
    public static async Task CreateFormIdListFile(string filePath, int recordCount = 100)
    {
        var lines = new List<string>();
        var plugins = new[] { "TestPlugin1.esp", "TestPlugin2.esp", "TestPlugin3.esp" };
        
        for (int i = 0; i < recordCount; i++)
        {
            var plugin = plugins[i % plugins.Length];
            var formId = $"{i:X6}";
            var entry = $"TestEntry_{i}";
            lines.Add($"{plugin}|{formId}|{entry}");
        }

        await File.WriteAllLinesAsync(filePath, lines);
    }

    /// <summary>
    /// Creates an invalid FormID list file with various formatting issues.
    /// </summary>
    public static async Task CreateInvalidFormIdListFile(string filePath)
    {
        var lines = new List<string>
        {
            "This is not a valid format",
            "Missing|pipes",
            "Too|Many|Pipes|Here|Extra",
            "", // Empty line
            "   ", // Whitespace only
            "Plugin.esp||EmptyFormId", // Empty FormID
            "Plugin.esp|123456|", // Empty entry
            "||", // All empty
            null! // Will be filtered out
        };

        await File.WriteAllLinesAsync(filePath, lines.Where(l => l != null));
    }

    /// <summary>
    /// Creates a plugin list file (plugins.txt format).
    /// </summary>
    public static async Task CreatePluginListFile(string filePath, params string[] plugins)
    {
        var lines = new List<string>();
        foreach (var plugin in plugins)
        {
            // Bethesda plugin list format
            lines.Add(plugin.StartsWith("*") ? plugin : $"*{plugin}");
        }
        
        await File.WriteAllLinesAsync(filePath, lines);
    }

    /// <summary>
    /// Creates test plugins of various sizes.
    /// </summary>
    public static class PluginSizes
    {
        public const int Small = 10;      // 10 FormIDs
        public const int Medium = 1000;   // 1,000 FormIDs
        public const int Large = 10000;   // 10,000 FormIDs
    }

    /// <summary>
    /// Gets the path to a test data file.
    /// </summary>
    public static string GetTestDataPath(params string[] pathParts)
    {
        var parts = new List<string> { TestDataRoot };
        parts.AddRange(pathParts);
        return Path.Combine(parts.ToArray());
    }

    /// <summary>
    /// Creates a complete test environment for a specific game.
    /// </summary>
    public static async Task<TestGameEnvironment> CreateTestGameEnvironment(string gameName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_game_{Guid.NewGuid()}");
        var gameDir = Path.Combine(tempDir, gameName);
        var dataDir = Path.Combine(gameDir, "Data");
        
        Directory.CreateDirectory(dataDir);
        
        var env = new TestGameEnvironment
        {
            RootDirectory = tempDir,
            GameDirectory = gameDir,
            DataDirectory = dataDir,
            PluginFiles = new List<string>()
        };

        // Create base game plugins based on game type
        switch (gameName.ToLower())
        {
            case "skyrim special edition":
            case "skyrim se":
                CreateMinimalPlugin(dataDir, "Skyrim.esm");
                CreateMinimalPlugin(dataDir, "Update.esm");
                CreateMinimalPlugin(dataDir, "Dawnguard.esm");
                env.PluginFiles.AddRange(new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm" });
                break;
                
            case "fallout 4":
            case "fallout4":
                CreateMinimalPlugin(dataDir, "Fallout4.esm");
                CreateMinimalPlugin(dataDir, "DLCRobot.esm");
                env.PluginFiles.AddRange(new[] { "Fallout4.esm", "DLCRobot.esm" });
                break;
                
            case "starfield":
                CreateMinimalPlugin(dataDir, "Starfield.esm");
                CreateMinimalPlugin(dataDir, "Constellation.esm");
                env.PluginFiles.AddRange(new[] { "Starfield.esm", "Constellation.esm" });
                break;
                
            case "oblivion":
                CreateMinimalPlugin(dataDir, "Oblivion.esm");
                env.PluginFiles.Add("Oblivion.esm");
                break;
        }

        // Create plugin list file
        var pluginListPath = Path.Combine(gameDir, "plugins.txt");
        await CreatePluginListFile(pluginListPath, env.PluginFiles.ToArray());
        
        return env;
    }

    /// <summary>
    /// Represents a test game environment.
    /// </summary>
    public class TestGameEnvironment : IDisposable
    {
        public string RootDirectory { get; init; } = string.Empty;
        public string GameDirectory { get; init; } = string.Empty;
        public string DataDirectory { get; init; } = string.Empty;
        public List<string> PluginFiles { get; init; } = new();

        public void AddPlugin(string pluginName, int formIdCount = 10)
        {
            CreateMinimalPlugin(DataDirectory, pluginName, formIdCount);
            PluginFiles.Add(pluginName);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                try { Directory.Delete(RootDirectory, true); } catch { }
            }
        }
    }

    /// <summary>
    /// Creates edge case test data for stress testing.
    /// </summary>
    public static class EdgeCases
    {
        public static async Task CreateLargeFormIdList(string filePath, int recordCount = 1000000)
        {
            using var writer = new StreamWriter(filePath);
            for (int i = 0; i < recordCount; i++)
            {
                await writer.WriteLineAsync($"LargePlugin.esp|{i:X6}|LargeEntry_{i}");
            }
        }

        public static void CreateCorruptedPlugin(string directory, string fileName)
        {
            var filePath = Path.Combine(directory, fileName);
            var random = new Random();
            var corruptedData = new byte[1024];
            random.NextBytes(corruptedData);
            File.WriteAllBytes(filePath, corruptedData);
        }

        public static async Task CreateFormIdListWithSpecialCharacters(string filePath)
        {
            var lines = new List<string>
            {
                @"Plugin.esp|000001|Entry with spaces",
                @"Plugin.esp|000002|Entry-with-dashes",
                @"Plugin.esp|000003|Entry_with_underscores",
                @"Plugin.esp|000004|Entry.with.dots",
                @"Plugin.esp|000005|Entry'with'quotes",
                @"Plugin.esp|000006|Entry""with""double""quotes",
                @"Plugin.esp|000007|Entry\with\backslashes",
                @"Plugin.esp|000008|Entry/with/forward/slashes",
                @"Plugin.esp|000009|Entry(with)parentheses",
                @"Plugin.esp|00000A|Entry[with]brackets",
                @"Plugin.esp|00000B|Entry{with}braces",
                @"Plugin.esp|00000C|Entry with 日本語",
                @"Plugin.esp|00000D|Entry with emoji 😀"
            };

            await File.WriteAllLinesAsync(filePath, lines);
        }
    }
}