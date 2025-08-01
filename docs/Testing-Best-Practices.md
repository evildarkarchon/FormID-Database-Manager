# FormID Database Manager - Testing Best Practices

## Overview

This document outlines the testing best practices specific to the FormID Database Manager project. These guidelines ensure consistent, maintainable, and efficient tests across the codebase.

## Project-Specific Testing Guidelines

### 1. Game Installation Dependencies

Many tests require actual game installations. We use custom attributes to handle these scenarios:

```csharp
// For tests that require game installations
[RequiresGameInstallationFact]
public async Task ProcessPlugin_ExtractsFormIDsFromRealPlugin()

// For tests that expect failures when games ARE installed
[ExpectsGameEnvironmentFailureFact]
public async Task ProcessPlugin_ThrowsException_WhenGameNotInstalled()
```

### 2. Database Testing Pattern

Always use in-memory SQLite for unit tests:

```csharp
public class MyDatabaseTest : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    
    public MyDatabaseTest(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }
}
```

### 3. Async Testing Patterns

All async operations should properly handle cancellation:

```csharp
[Fact]
public async Task MyMethod_CancelsCleanly_WhenTokenSignaled()
{
    using var cts = new CancellationTokenSource();
    var task = service.ProcessAsync(cts.Token);
    
    cts.Cancel();
    
    await Assert.ThrowsAsync<OperationCanceledException>(() => task);
}
```

### 4. Mock Configuration

Use the MockFactory for consistent mock setups:

```csharp
var mockFactory = new MockFactory();
var mockGameDetection = mockFactory.CreateGameDetectionServiceMock(GameRelease.SkyrimSE);
var mockDatabase = mockFactory.CreateDatabaseServiceMock();
```

### 5. Test Data Management

Store test data in the appropriate directories:
- `TestData/Plugins/` - Sample plugin files by game
- `TestData/PluginLists/` - Sample plugin list files
- `TestData/FormIdFiles/` - Sample FormID text files

### 6. UI Testing with Avalonia.Headless

Always use the `[AvaloniaFact]` attribute for UI tests:

```csharp
[AvaloniaFact]
public void Control_BehavesCorrectly_UnderCondition()
{
    var window = new MainWindow();
    var control = window.FindControl<Button>("ProcessButton");
    
    Assert.NotNull(control);
    Assert.True(control.IsEnabled);
}
```

### 7. Performance Testing Guidelines

#### Benchmarks
Use BenchmarkDotNet attributes appropriately:

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class MyBenchmark
{
    [Params(100, 1000, 10000)]
    public int RecordCount { get; set; }
    
    [Benchmark]
    public async Task ProcessRecords()
    {
        // Benchmark code
    }
}
```

#### Load Tests
Always include resource monitoring:

```csharp
[Fact]
[Trait("Category", "LoadTest")]
public async Task ProcessManyPlugins_HandlesLoadEfficiently()
{
    var memoryBefore = GC.GetTotalMemory(true);
    
    // Perform load test
    
    var memoryAfter = GC.GetTotalMemory(true);
    var memoryIncrease = memoryAfter - memoryBefore;
    
    Assert.True(memoryIncrease < 100_000_000, // 100MB
        $"Memory increased by {memoryIncrease:N0} bytes");
}
```

### 8. Test Collections and Parallelization

Use appropriate test collections to control parallelization:

```csharp
[Collection("Database Tests")] // Sequential execution
public class DatabaseIntensiveTest { }

[Collection("UI Tests")] // Sequential execution
public class UITest { }

// Unit tests run in parallel by default (no collection attribute)
public class FastUnitTest { }
```

### 9. Error Message Testing

When testing error handling, verify the exact error messages:

```csharp
[Fact]
public void Method_AddsSpecificError_OnFailure()
{
    var messages = new List<string>();
    var errorCallback = (string msg) => messages.Add(msg);
    
    service.Process(errorCallback);
    
    Assert.Contains("Expected error message", messages);
}
```

### 10. Progress Reporting Tests

Always test progress reporting with deterministic values:

```csharp
[Fact]
public async Task Process_ReportsProgressAccurately()
{
    var progressValues = new List<int>();
    var progress = new Progress<int>(p => progressValues.Add(p));
    
    await service.ProcessAsync(progress);
    
    Assert.Equal(new[] { 0, 25, 50, 75, 100 }, progressValues);
}
```

## Test Naming Conventions

Follow the pattern: `MethodName_StateUnderTest_ExpectedBehavior`

Examples:
- `InitializeDatabase_CreatesTableForEachGameRelease`
- `ProcessPlugins_CancelsCleanly_WhenTokenSignaled`
- `SelectGameDirectory_ReturnsNull_WhenNoFolderSelected`

## Common Pitfalls to Avoid

### 1. File System Dependencies
❌ Don't use hard-coded paths:
```csharp
var path = @"C:\Games\Skyrim";
```

✅ Use test fixtures or builders:
```csharp
using var tempDir = TestOptimization.CreateTempDirectory();
var path = Path.Combine(tempDir.Path, "TestGame");
```

### 2. Database Connection Leaks
❌ Don't forget to dispose connections:
```csharp
var connection = new SQLiteConnection(connectionString);
// Missing using or dispose
```

✅ Always use using statements:
```csharp
using var connection = new SQLiteConnection(connectionString);
```

### 3. Synchronous UI Operations
❌ Don't perform UI operations synchronously:
```csharp
button.Click += (s, e) => ProcessData();
```

✅ Use async event handlers:
```csharp
button.Click += async (s, e) => await ProcessDataAsync();
```

### 4. Uncontrolled Parallelization
❌ Don't let database tests run in parallel:
```csharp
public class DatabaseTest // No collection
```

✅ Use appropriate collections:
```csharp
[Collection("Database Tests")]
public class DatabaseTest
```

## Test Optimization Techniques

### 1. Shared Resources
Use the TestOptimization helper for expensive resources:

```csharp
var gameData = await TestOptimization.GetOrCreateSharedResourceAsync(
    "skyrim-test-data",
    async () => await LoadTestDataAsync()
);
```

### 2. Database Pooling
Reuse test databases when possible:

```csharp
var dbPath = TestOptimization.DatabasePool.GetDatabase();
try
{
    // Use database
}
finally
{
    TestOptimization.DatabasePool.ReturnDatabase(dbPath);
}
```

### 3. Timeout Protection
Prevent hanging tests:

```csharp
await TestOptimization.RunWithTimeoutAsync(
    async () => await LongRunningOperation(),
    TimeSpan.FromSeconds(30)
);
```

## Integration with CI/CD

### GitHub Actions Configuration
Tests are automatically run on:
- Every push
- Pull requests
- Three platforms: Windows, Linux, macOS

### Test Categories
Use appropriate categories for conditional execution:
- `Unit` - Fast, isolated tests
- `Integration` - Tests with external dependencies
- `LoadTest` - Performance under load
- `StressTest` - Extreme conditions
- `UI` - UI interaction tests

## Debugging Failed Tests

### 1. Check Skip Reasons
Some tests skip when games aren't installed:
```bash
dotnet test --logger "console;verbosity=detailed"
```

### 2. Enable Diagnostic Output
For detailed test output:
```bash
dotnet test --diag:testlog.txt
```

### 3. Run Specific Tests
To debug a single test:
```bash
dotnet test --filter "FullyQualifiedName~MyTestName"
```

### 4. Check Test Artifacts
Failed UI tests may produce screenshots in:
`TestResults/Screenshots/`

## Maintenance Schedule

### Daily
- Monitor CI/CD test results
- Address any failing tests

### Weekly
- Review test execution times
- Update skipped test conditions

### Monthly
- Analyze code coverage trends
- Review and update test data

### Quarterly
- Performance baseline updates
- Test strategy review

## Contributing New Tests

When adding new tests:
1. Follow the naming conventions
2. Use appropriate test collections
3. Include both positive and negative test cases
4. Test cancellation for async operations
5. Verify error handling and logging
6. Consider performance implications
7. Update this document if introducing new patterns