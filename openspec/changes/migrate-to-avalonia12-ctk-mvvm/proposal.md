# Migrate to Avalonia 12 + CommunityToolkit.Mvvm

## Why

The app is pinned to Avalonia 11.3.11 with hand-rolled `INotifyPropertyChanged` boilerplate in every view-model and model, and the test project still carries an `Avalonia.ReactiveUI` 11.3.9 dependency (with a `.UseReactiveUI()` call in `TestInitialization.cs`) even though the main app contains no ReactiveUI code. This creates three problems: we are falling behind the Avalonia release train, we pay for an MVVM framework (ReactiveUI) that nothing actually uses, and the MVVM layer we *do* use is a home-grown `SetProperty<T>` helper per class. Moving to Avalonia 12 unlocks the current feature/perf line, and adopting `CommunityToolkit.Mvvm` source generators replaces the manual backing-field boilerplate with `[ObservableProperty]` / `[RelayCommand]` while keeping the code 100% trim- and AOT-friendly.

## What Changes

- **BREAKING (build):** Bump every `Avalonia.*` package in `FormID Database Manager.csproj` and `FormID Database Manager.Tests.csproj` from `11.3.11` / `11.3.9` to Avalonia 12's current stable line. Update any API renames / namespace shifts surfaced by the bump.
- **BREAKING (tests):** Remove the `Avalonia.ReactiveUI` package reference from the test project and the `.UseReactiveUI()` call in `FormID Database Manager.Tests/TestInitialization.cs`. Tests that rely on scheduling should use `SynchronousThreadDispatcher` (already in `FormID Database Manager.TestUtilities`).
- Add `CommunityToolkit.Mvvm` package reference (current stable) to the main project.
- Replace the hand-rolled `INotifyPropertyChanged` plumbing in `ViewModels/MainWindowViewModel.cs` and `Models/PluginListItem.cs` with `ObservableObject` + `[ObservableProperty]` source generators. Preserve existing behavior: `IsGameSelected` / `HasMultipleDirectories` / `IsProgressVisible` dependent-property notifications, the `LockedObservableCollection<T>` contract, the filter debounce + reentrancy guard (`Interlocked`), and UI-thread marshalling via `IThreadDispatcher`.
- Keep `MainWindow.axaml.cs`'s code-behind-driven wiring (no DI container) but let it bind to the generated public properties unchanged in name.
- Update `AXAML` compiled bindings (`x:DataType`) if Avalonia 12 tightens binding generation; otherwise leave markup untouched.
- Verify trimming / `PublishSingleFile` publish still succeeds with the new dependency graph; adjust `TrimmerRootAssembly` entries if CommunityToolkit.Mvvm requires it.
- Update `AGENTS.md` / `CLAUDE.md` tech-stack lines to reflect Avalonia 12 + CommunityToolkit.Mvvm and drop the ReactiveUI mention from testing conventions.

## Capabilities

### New Capabilities
- `mvvm-framework`: Defines which MVVM backend the app uses, what the ViewModel base-class contract is, how dependent ("computed") properties are wired, and the rules tests must follow for Avalonia app initialization (no ReactiveUI scheduler).

### Modified Capabilities

_None._ The four existing specs (`game-release-support`, `mutagen-load-order-snapshot`, `mutagen-separated-master-overlay-read`, `sqlite-wal-configuration`) describe domain/data behavior that is framework-agnostic; this migration does not change any of their requirements.

## Impact

- **Packages:** `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics`, `Avalonia.Headless.XUnit` bumped to Avalonia 12. `Avalonia.ReactiveUI` removed. `CommunityToolkit.Mvvm` added.
- **Code (main project):**
  - `FormID Database Manager/FormID Database Manager.csproj`
  - `FormID Database Manager/ViewModels/MainWindowViewModel.cs`
  - `FormID Database Manager/Models/PluginListItem.cs`
  - `FormID Database Manager/App.axaml` + `App.axaml.cs` (only if Avalonia 12 API changes require it)
  - `FormID Database Manager/Program.cs` (only if `AppBuilder` API changed)
  - `FormID Database Manager/MainWindow.axaml` + `.axaml.cs` (binding/namespace touch-ups if needed)
- **Code (test project):**
  - `FormID Database Manager.Tests/FormID Database Manager.Tests.csproj`
  - `FormID Database Manager.Tests/TestInitialization.cs`
  - Any UI test fixtures that transitively touched ReactiveUI (none expected).
- **Build/publish:** `dotnet publish -c Release -r win-x64` must still produce a trimmed single-file self-contained binary. Trim warnings from CommunityToolkit.Mvvm generators must be zero (they are source-generated, so this should hold).
- **CI / tooling:** No changes expected, but `docs/mutagen/` and the `Mutagen/` git submodule are NOT touched — they are read-only reference material.
- **Docs:** `AGENTS.md` "Tech Stack" section and testing-conventions bullet.
