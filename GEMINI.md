# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FormID Database Manager is an Avalonia UI desktop application that creates SQLite databases storing FormIDs and their associated EditorID/Name values from Bethesda game plugins (Skyrim, Fallout 4, Starfield, Oblivion). It uses the Mutagen library to parse plugin files.

## Build & Run Commands

```bash
# Build (solution file uses .slnx format)
dotnet build "FormID Database Manager.slnx"

# Run the application
dotnet run --project "FormID Database Manager"

# Run all tests
dotnet test "FormID Database Manager.Tests"

# Run a specific test class
dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~DatabaseServiceTests"

# Run a single test
dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~DatabaseServiceTests.InitializeDatabase_CreatesTableForEachGameRelease"

# Run tests by category
dotnet test "FormID Database Manager.Tests" --filter "Category=LoadTest"

# Publish (self-contained, trimmed single file)
dotnet publish "FormID Database Manager" -c Release -r win-x64
```

## Tech Stack

- **.NET 10.0** / C# with nullable enabled
- **Avalonia UI 11.3** (cross-platform desktop UI, AXAML files)
- **Mutagen 0.51.5** (Bethesda plugin parsing)
- **Microsoft.Data.Sqlite** (database)
- **xUnit** + **Moq** + **Avalonia.Headless.XUnit** (testing)
- **BenchmarkDotNet** (performance benchmarks)

## Architecture

The solution has three projects in `FormID Database Manager.slnx`:

### Main App (`FormID Database Manager/`)
- `MainWindow.axaml.cs` — UI event handlers, wires services together manually (no DI container)
- `ViewModels/MainWindowViewModel.cs` — INotifyPropertyChanged ViewModel with plugin filtering, progress tracking, and thread-safe UI updates via `IThreadDispatcher`
- `Services/` — All business logic:
  - `DatabaseService` — SQLite schema, CRUD, optimizations (WAL mode, covering indexes)
  - `PluginProcessingService` — Orchestrates plugin processing with cancellation support
  - `ModProcessor` — Parses plugins via Mutagen's `CreateFromBinaryOverlay`, extracts FormIDs with batched inserts (1000/batch). Uses cached reflection for name extraction
  - `FormIdTextProcessor` — Parses pipe-delimited text files (`plugin|formid|entry`), batched inserts (10000/batch)
  - `PluginListManager` — Loads plugin lists from game directories on background thread
  - `GameDetectionService` — Detects game type from directory structure (master file presence)
  - `WindowManager` — Avalonia file/folder picker dialogs
  - `IThreadDispatcher` / `AvaloniaThreadDispatcher` — Abstraction for UI thread marshalling (testable)
- `Models/` — `PluginListItem` (INotifyPropertyChanged), `ProcessingParameters`

### Test Utilities (`FormID Database Manager.TestUtilities/`)
- `Fixtures/DatabaseFixture` — Shared in-memory SQLite setup
- `Mocks/MockFactory` — Consistent mock creation for services
- `Mocks/SynchronousThreadDispatcher` — Test-friendly IThreadDispatcher (executes immediately)
- `Mocks/SynchronousProgress` — Synchronous IProgress for testing
- `Builders/` — Test data builders (GameDetectionBuilder, PluginBuilder)
- Custom test attributes: `RequiresGameInstallationAttribute`, `ExpectsGameEnvironmentFailureAttribute`

### Tests (`FormID Database Manager.Tests/`)
- `Unit/Services/` — Service unit tests
- `Unit/ViewModels/` — ViewModel tests
- `UI/` — Avalonia headless UI tests (use `[AvaloniaFact]`)
- `Integration/` — Tests with real database/Mutagen (some require game installations)
- `Performance/` — Benchmarks, load tests, stress tests, regression tests

## Key Patterns

- **Thread safety**: UI updates go through `IThreadDispatcher`. ViewModel uses `Interlocked` for filter reentrancy guard and `lock` for plugins collection access.
- **SQL injection prevention**: `GetSafeTableName()` uses an explicit whitelist switch on `GameRelease` enum — this pattern is duplicated in `DatabaseService`, `ModProcessor`, and `FormIdTextProcessor.BatchInserter`.
- **Cancellation**: All async processing supports `CancellationToken`. `PluginProcessingService` manages `CancellationTokenSource` lifecycle with a lock. Note: Mutagen's `CreateFromBinaryOverlay` is synchronous and not cancellable.
- **Test collections**: Database and UI tests use `[Collection(...)]` for sequential execution. Unit tests run in parallel.
- **InternalsVisibleTo**: The main project exposes internals to the test project.

## Testing Conventions

- Test naming: `MethodName_StateUnderTest_ExpectedBehavior`
- Use `SynchronousThreadDispatcher` in tests instead of `AvaloniaThreadDispatcher`
- Use in-memory SQLite via `DatabaseFixture` for database tests
- Integration tests requiring game installations use `[RequiresGameInstallationFact]`
- UI tests use `[AvaloniaFact]` attribute

## Important Notes

- The `Mutagen/` directory is a **git submodule** included as a **read-only API reference** for AI agents and developers. It is not part of the solution build and should never be modified. The app references Mutagen via NuGet package, not the submodule source. Use it to look up Mutagen types, interfaces, and method signatures when needed.
- `CS1998` (async method lacks await) is treated as an error via `WarningsAsErrors`.
- The app targets `net10.0` (upgraded from net8.0; README still references .NET 8).
