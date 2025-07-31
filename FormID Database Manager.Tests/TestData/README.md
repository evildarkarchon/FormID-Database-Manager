# Test Data Directory

This directory contains test data used by the unit and integration tests. The test data is organized as follows:

## Directory Structure

```
TestData/
├── Plugins/                # Mock plugin files for different games
│   ├── Skyrim/            # Skyrim test plugins
│   ├── Fallout4/          # Fallout 4 test plugins
│   ├── Starfield/         # Starfield test plugins
│   └── Oblivion/          # Oblivion test plugins
├── FormIdLists/           # FormID list test files
│   ├── Valid/             # Valid format FormID lists
│   ├── Invalid/           # Invalid format FormID lists
│   └── EdgeCases/         # Edge case FormID lists
└── PluginLists/           # Plugin list files (plugins.txt format)
```

## Test Data Builder

The `TestDataBuilder.cs` class provides methods to generate test data programmatically:

- `CreateMinimalPlugin()` - Creates minimal valid ESP/ESM files
- `CreateFormIdListFile()` - Creates FormID list files
- `CreatePluginListFile()` - Creates plugin list files
- `CreateTestGameEnvironment()` - Creates complete test game environments

## Usage in Tests

```csharp
// Ensure test data structure exists
TestDataBuilder.EnsureTestDataStructure();

// Create a test plugin
TestDataBuilder.CreateMinimalPlugin(testDir, "TestPlugin.esp", formIdCount: 100);

// Create a test FormID list
await TestDataBuilder.CreateFormIdListFile(testPath, recordCount: 1000);

// Create a complete test environment
using var env = await TestDataBuilder.CreateTestGameEnvironment("Skyrim Special Edition");
// Use env.DataDirectory, env.PluginFiles, etc.
```

## Notes

- Test data files are generated programmatically to avoid binary files in source control
- The TestDataBuilder class handles cleanup via IDisposable patterns
- Edge cases include large files, special characters, and corrupted data