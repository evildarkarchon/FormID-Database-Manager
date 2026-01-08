# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FormID Database Manager is a cross-platform desktop application built with Avalonia UI that creates SQLite databases containing FormIDs and their associated EditorID/Name values from Bethesda game plugins. It supports Skyrim (SE/AE/VR/GOG), Fallout 4, Starfield, and Oblivion.

**Technical Stack:**
- C# 12.0 with .NET 10.0
- Nullable reference types enabled
- Compiled bindings for Avalonia
- **Warning CS1998 treated as error**: Async methods without await operators will cause build failures

## Build Commands

```bash
# Build the project
dotnet build

# Run the application  
dotnet run --project "FormID Database Manager/FormID Database Manager.csproj"

# Run all tests
dotnet test

# Run specific test project
dotnet test "FormID Database Manager.Tests/FormID Database Manager.Tests.csproj"

# Run a single test
dotnet test --filter "FullyQualifiedName~DatabaseServiceTests.InitializeDatabase_CreatesCorrectTable"

# Check code formatting
dotnet format --verify-no-changes

# Fix code formatting
dotnet format

# Publish for release (with DLL organization)
dotnet publish -c Release
```

## Code Coverage Commands

```bash
# Run tests with code coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura

# Run tests with coverage and generate report file
dotnet test /p:CollectCoverage=true /p:CoverletOutput=./coverage.xml /p:CoverletOutputFormat=cobertura

# Run tests with coverage showing results in console
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov

# Run specific test category with coverage
dotnet test --filter "Category=Unit" /p:CollectCoverage=true

# Exclude test files from coverage
dotnet test /p:CollectCoverage=true /p:Exclude="[*Tests*]*"
```

## Performance Testing

```bash
# Run performance benchmarks
dotnet run -c Release --project "FormID Database Manager.Tests/FormID Database Manager.Tests.csproj" -- --filter "*Benchmark*"

# Run specific benchmark
dotnet run -c Release --project "FormID Database Manager.Tests/FormID Database Manager.Tests.csproj" -- --filter "DatabaseBenchmarks"

# Run load tests
dotnet test --filter "Category=LoadTest"

# Run stress tests  
dotnet test --filter "Category=StressTest"
```

## Architecture

The application follows MVVM pattern with these key components:

### Services (Business Logic)
- **DatabaseService**: Manages SQLite database operations with game-specific tables
- **GameDetectionService**: Auto-detects game type from plugin directory  
- **ModProcessor**: Processes individual plugins using Mutagen library
- **PluginProcessingService**: Orchestrates the entire processing workflow
- **FormIdTextProcessor**: Filters FormID text files based on plugin lists
- **PluginListManager**: Manages plugin list loading and parsing
- **WindowManager**: Window positioning and management utilities

### Key Design Patterns
- Dependency injection for services
- Async/await throughout for UI responsiveness
- Error callbacks with ignorable error patterns
- Batch database operations for performance
- Custom assembly resolver for DLL loading from libs folder
- IThreadDispatcher abstraction for UI thread access (enables testability)

### Database Schema
Each game gets its own table:
```sql
CREATE TABLE {GameRelease} (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    plugin TEXT NOT NULL,
    formid TEXT NOT NULL,
    entry TEXT NOT NULL
)
```

## Important Implementation Details

1. **DLL Resolution**: Program.cs contains custom assembly resolver that loads DLLs from `libs` folder. The PostPublish build target automatically organizes all dependency DLLs into the `libs` directory during `dotnet publish`.
2. **Game Detection**: Uses directory name and plugin files to auto-detect game type
3. **Batch Processing**: Database inserts are batched (1000 entries) for performance
4. **Error Handling**: Ignorable errors are defined in GameDetectionService for known issues
5. **UI Threading**: Heavy operations use Task.Run to avoid blocking UI
6. **Acrylic Effect**: MainWindow uses platform-specific acrylic blur for modern appearance
7. **Async Method Warning**: CS1998 warning (async without await) is configured as an error. Remove the `async` keyword from methods that don't use `await`.

## Testing Strategy

### Test Categories
- **Unit Tests**: Located in `FormID Database Manager.Tests/Unit/`
  - Service tests with mocked dependencies
  - ViewModel tests for business logic
  - Model tests for data validation
  
- **Integration Tests**: Located in `FormID Database Manager.Tests/Integration/`
  - End-to-end service tests with real dependencies
  - Database integration tests
  - Plugin processing workflow tests
  
- **UI Tests**: Located in `FormID Database Manager.Tests/UI/`
  - Use Avalonia.Headless.XUnit for headless UI testing
  - MainWindow initialization and control tests
  - Data binding verification tests
  - UI control behavior tests
  
- **Performance Tests**: Located in `FormID Database Manager.Tests/Performance/`
  - **Benchmarks**: Using BenchmarkDotNet for micro-benchmarks
    - DatabaseBenchmarks: Database operation performance
    - PluginProcessingBenchmarks: Plugin processing speed
    - MemoryBenchmarks: Memory usage analysis
  - **Load Tests**: Testing under heavy load (Category=LoadTest)
    - 100+ concurrent plugin processing
    - Large plugin handling (100k+ FormIDs)
    - Concurrent database operations
  - **Stress Tests**: Testing extreme conditions (Category=StressTest)
    - Rapid cancellation scenarios
    - Maximum connection limits
    - Memory pressure scenarios

- **Test Utilities**: Shared test builders and mocks in `FormID Database Manager.TestUtilities/`
  - `SynchronousThreadDispatcher`: Test-friendly dispatcher that executes actions immediately (avoids UI thread deadlocks)
  - `SynchronousProgress<T>`: Synchronous IProgress implementation for reliable test assertions

### Known Test Runner Quirk
**Running integration tests in isolation via `--filter` may hang**, but the full test suite runs successfully. This appears to be an environmental/test runner behavior rather than a code issue:
- `dotnet test` (full suite): Works correctly, all tests pass
- `dotnet test --filter "FullyQualifiedName~IntegrationTests"`: May hang indefinitely
- Individual tests via filter work: `dotnet test --filter "FullyQualifiedName~DatabaseIntegrationTests.Database_RecoverFromCorruption_Successfully"`

**Workaround**: Run the full test suite instead of filtering to integration test classes.

### Coverage Goals
- Target: 80% code coverage
- Use coverlet for coverage analysis
- Exclude test projects from coverage metrics

## Key Dependencies

- **Avalonia UI 11.3.9**: Cross-platform UI framework
- **Mutagen.Bethesda 0.51.5**: For parsing Bethesda game plugins
- **Microsoft.Data.Sqlite 10.0.0**: Database operations (lightweight, modern SQLite provider)
- **xUnit 2.9.3**: Testing framework with Moq for mocking
- **BenchmarkDotNet 0.15.8**: Performance benchmarking

## Development Notes

### Mutagen API Documentation
- There is no dedicated API documentation for Mutagen
- API queries must reference the source code at `https://github.com/Mutagen-Modding/Mutagen/tree/0.51.5` (match current version)

### Project License
- GPL-3.0 License - modifications and distributions must comply with GPL-3.0 terms