# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FormID Database Manager is a WinUI desktop application that creates SQLite databases storing FormIDs and their associated EditorID/Name values from Bethesda game plugins (Skyrim, Fallout 4, Starfield, Oblivion). It uses the Mutagen library to parse plugin files.

## Build & Run Commands

```bash
# Build (solution file uses .slnx format)
dotnet build "FormID Database Manager.slnx"

# Run the application
dotnet run --project "FormID Database Manager.WinUI" -p:Platform=x64

# Run all tests
dotnet test "FormID Database Manager.Tests"

# Run a specific test class
dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~DatabaseServiceTests"

# Run a single test
dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~DatabaseServiceTests.InitializeDatabase_CreatesTableForEachGameRelease"

# Run tests by category
dotnet test "FormID Database Manager.Tests" --filter "Category=LoadTest"

# Publish the WinUI app
pwsh ./scripts/publish-portable.ps1
```

## Tech Stack

- **.NET 10.0** / C# with nullable enabled
- **WinUI 3 / Windows App SDK** (Windows desktop UI, XAML files)
- **CommunityToolkit.Mvvm 8.x** (MVVM source generators)
- **Mutagen 0.51.5** (Bethesda plugin parsing)
- **Microsoft.Data.Sqlite** (database)
- **xUnit** + **Moq** (testing)
- **BenchmarkDotNet** (performance benchmarks)

## Architecture

The solution has four projects in `FormID Database Manager.slnx`:

### Core (`FormID Database Manager.Core/`)
- `ViewModels/MainWindowViewModel.cs` — CommunityToolkit.Mvvm ObservableObject ViewModel with plugin filtering, progress tracking, and thread-safe UI updates via `IThreadDispatcher`
- `Services/` — All business logic:
  - `DatabaseService` — SQLite schema, CRUD, optimizations (WAL mode, covering indexes)
  - `PluginProcessingService` — Orchestrates plugin processing with cancellation support
  - `ProcessingRun` — External seam for Plugin Ingestion and FormID text-file Processing Runs with structured status, warning, and error events
  - `PluginIngestion` — Internal one-Plugin ingestion module; reads Mutagen overlays via `IPluginOverlayReader`, extracts Entries via `EntryExtraction`, and writes through `FormIdRecordStore`
  - `FormIdRecordStore` — Owns SQLite writes and pipe-delimited FormID text-file imports (`plugin|formid|entry`) with 10000-row staging batches
  - `PluginListManager` — Loads plugin lists from game directories on background thread
  - `GameDetectionService` — Detects game type from directory structure (master file presence)
  - `IFileDialogService` — UI-neutral file/folder picker abstraction
  - `IThreadDispatcher` / `ImmediateThreadDispatcher` / `QueuedThreadDispatcher` — Abstractions for UI thread marshalling (testable)
- `Models/` — `PluginListItem` (CommunityToolkit.Mvvm ObservableObject), `ProcessingParameters`

### Main App (`FormID Database Manager.WinUI/`)
- `App.xaml.cs` — WinUI application startup
- `MainWindow.xaml` / `MainWindow.xaml.cs` — WinUI workflow surface and event handlers
- `Services/WinUiFileDialogService.cs` — Windows App SDK picker implementation
- `Services/WinUiThreadDispatcher.cs` — WinUI dispatcher implementation

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
- `Unit/Architecture/` — Core/WinUI source-boundary and wiring tests
- `Integration/` — Tests with real database/Mutagen (some require game installations)
- `Performance/` — Benchmarks, load tests, stress tests, regression tests

## Key Patterns

- **Thread safety**: UI updates go through `IThreadDispatcher`. ViewModel uses `Interlocked` for filter reentrancy guard and `lock` for plugins collection access.
- **SQL injection prevention**: `GameReleaseHelper.GetSafeTableName()` uses an explicit whitelist switch on `GameRelease` enum.
- **Cancellation**: All async processing supports `CancellationToken`. `PluginProcessingService` manages `CancellationTokenSource` lifecycle with a lock. Note: Mutagen's `CreateFromBinaryOverlay` is synchronous and not cancellable.
- **Test collections**: Database tests use `[Collection(...)]` for sequential execution. Unit tests run in parallel.
- **InternalsVisibleTo**: The core project exposes internals to the test and WinUI projects.

## Testing Conventions

- Test naming: `MethodName_StateUnderTest_ExpectedBehavior`
- Use `SynchronousThreadDispatcher` in tests instead of platform dispatchers
- Use in-memory SQLite via `DatabaseFixture` for database tests
- Integration tests requiring game installations use `[RequiresGameInstallationFact]`

## Important Notes

- **Mutagen API Reference**: When looking up Mutagen types, interfaces, or method signatures, consult `docs/mutagen/` first — it contains pre-generated API documentation (one markdown file per project) with organized type catalogs, method signatures, and common patterns. If the documentation doesn't have the detail you need, fall back to the `Mutagen/` git submodule source code as a secondary reference. The submodule is read-only and should never be modified. The app references Mutagen via NuGet package, not the submodule source.
- `CS1998` (async method lacks await) is treated as an error via `WarningsAsErrors`.
- Comments are welcome and encouraged; this project overrides the default "no comments" agent rule. Prefer WHY-comments over WHAT-comments — explain non-obvious decisions, invariants, and the reasoning behind intentional patterns (e.g. sync-over-async in disposal, sequential-only cleaning, the single process slot). Do not strip accurate existing comments as cleanup. Add XML doc comments (`///`) on new or substantially rewritten public members unless trivial.

## Agent skills

### Issue tracker

Track work and PRDs as GitHub Issues in `evildarkarchon/FormID-Database-Manager`; external pull requests are not a triage request surface. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the canonical triage-state labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, and `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Use the single-context layout rooted at `CONTEXT.md`, with architectural decisions under `docs/adr/`. See `docs/agents/domain.md`.
