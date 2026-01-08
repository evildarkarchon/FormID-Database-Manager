# FormID Database Manager

## Project Overview
**FormID Database Manager** is a cross-platform desktop application designed to parse Bethesda game plugins (Skyrim, Fallout 4, Starfield, Oblivion) and create SQLite databases mapping FormIDs to EditorIDs/Names.

- **Type:** Desktop Application (GUI)
- **Framework:** Avalonia UI (.NET 8.0)
- **Language:** C# 12.0
- **Key Library:** Mutagen.Bethesda (Plugin parsing)
- **Architecture:** MVVM (Model-View-ViewModel)

## Build & Run

### Prerequisites
- .NET 8.0 SDK

### Core Commands
- **Build:**
  ```bash
  dotnet build
  ```
- **Run Application:**
  ```bash
  dotnet run --project "FormID Database Manager/FormID Database Manager.csproj"
  ```
- **Run All Tests:**
  ```bash
  dotnet test
  ```
- **Format Code:**
  ```bash
  dotnet format
  ```
- **Publish (Release):**
  ```bash
  dotnet publish -c Release
  ```
  *Note: Post-publish tasks automatically organize dependency DLLs into a `libs` folder.*

### Advanced Testing
Refer to `docs/Testing-Best-Practices.md` for detailed guidelines.
- **Run Specific Test Project:**
  ```bash
  dotnet test "FormID Database Manager.Tests/FormID Database Manager.Tests.csproj"
  ```
- **Run Performance Benchmarks:**
  ```bash
  dotnet run -c Release --project "FormID Database Manager.Tests/FormID Database Manager.Tests.csproj" -- --filter "*Benchmark*"
  ```
- **Coverage:**
  ```bash
  dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
  ```

## Development Conventions

### Architecture
- **Pattern:** MVVM.
- **Services:** Business logic (e.g., `DatabaseService`, `PluginProcessingService`) is injected via Dependency Injection.
- **UI:** Avalonia XAML with compiled bindings. Platform-specific features like Acrylic blur are used.
- **Async/Await:** Heavy operations (database, parsing) run on background threads. **Warning CS1998 is treated as an error**, so ensure all `async` methods have an `await`.

### Database Schema
Each supported game release gets its own table in the SQLite database:
```sql
CREATE TABLE {GameRelease} (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    plugin TEXT NOT NULL,
    formid TEXT NOT NULL,
    entry TEXT NOT NULL
)
```

### Implementation Details
- **DLL Resolution:** A custom assembly resolver in `Program.cs` loads dependencies from a `libs` subdirectory to keep the root clean.
- **Game Detection:** `GameDetectionService` auto-detects games based on directory structure and plugin files.
- **Batching:** Database insertions are batched (1000 entries) for performance.

### Testing
- **Unit Tests:** In-memory SQLite, mocked services.
- **Integration Tests:** Real dependencies, end-to-end flows.
- **UI Tests:** Headless Avalonia testing (`Avalonia.Headless.XUnit`).
- **Test Utilities:**
  - `SynchronousThreadDispatcher`: Test-friendly dispatcher that avoids UI thread deadlocks.
  - `SynchronousProgress<T>`: Synchronous IProgress implementation for reliable assertions.
- **Attributes:**
  - `[RequiresGameInstallationFact]`: Skips tests if the actual game is not installed.
  - `[ExpectsGameEnvironmentFailureFact]`: Validates failure modes when games *are* installed.
  - `[AvaloniaFact]`: Required for UI tests.
- **Known Quirk:** Running integration tests in isolation via `--filter` may hang. Run the full test suite (`dotnet test`) instead.

## Key Files & Directories
- `FormID Database Manager/`: Main application source.
  - `App.axaml`: App entry and styles.
  - `Program.cs`: Entry point, DI setup, custom assembly resolver.
  - `Services/`: Core logic.
  - `ViewModels/`: UI state and logic.
- `FormID Database Manager.Tests/`: Comprehensive test suite.
- `docs/`: Detailed documentation (`Testing-Best-Practices.md`).
- `AGENTS.md`: Reference for build/test commands and context.
  
## API Information
- `Mutagen` does not have official API documentation, you will have to scan the source code from `https://github.com/Mutagen-Modding/Mutagen/tree/0.51.5`