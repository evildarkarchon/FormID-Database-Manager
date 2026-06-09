## Context

FormID Database Manager is a single-window Avalonia desktop app that ingests Bethesda plugin files via Mutagen and writes FormIDs into SQLite. All UI state is owned by one view-model, `MainWindowViewModel`, which hand-rolls `INotifyPropertyChanged` via a `SetProperty<T>` helper, a `LockedObservableCollection<T>`, an `IThreadDispatcher` abstraction for UI-thread marshalling, and an `Interlocked`-guarded filter reentrancy mechanism with a debounce `CancellationTokenSource`. `Models/PluginListItem` also uses the `field` keyword + manual `OnPropertyChanged`.

The current stack is:

- `FormID Database Manager.csproj`: `Avalonia 11.3.11`, `Avalonia.Desktop 11.3.11`, `Avalonia.Themes.Fluent 11.3.11`, `Avalonia.Fonts.Inter 11.3.11`, `Avalonia.Diagnostics 11.3.11` (debug only), plus Mutagen / Sqlite.
- `FormID Database Manager.Tests.csproj`: `Avalonia 11.3.11`, `Avalonia.Themes.Fluent 11.3.11`, `Avalonia.Headless.XUnit 11.3.11`, and `Avalonia.ReactiveUI 11.3.9`.
- `FormID Database Manager.Tests/TestInitialization.cs` calls `.UseReactiveUI()` inside `AppBuilder.Configure<TestApp>()` even though no production code depends on ReactiveUI.
- `AvaloniaUseCompiledBindingsByDefault` is already enabled; AXAML uses `x:DataType`.
- The project publishes as trimmed (`PublishTrimmed=true`, `TrimMode=partial`) single-file self-contained.

Constraints:

- `WarningsAsErrors=CS1998` must continue to pass.
- Trimming / single-file publish for `win-x64` must keep working.
- `[InternalsVisibleTo("FormID Database Manager.Tests")]` + the `SynchronousThreadDispatcher` must keep working unchanged.
- The four existing domain specs and their requirements are untouched.
- The `Mutagen/` git submodule is read-only — its ReactiveUI usage is irrelevant.

Stakeholders: the sole consumer is the local desktop user; there is no shipped API surface.

## Goals / Non-Goals

**Goals:**

- Upgrade all `Avalonia.*` packages in both projects to the current Avalonia 12 stable line and fix any compile / runtime breakage.
- Introduce `CommunityToolkit.Mvvm` as the MVVM backbone for the main project, and rewrite `MainWindowViewModel` + `PluginListItem` to use `ObservableObject` and `[ObservableProperty]` source generators without losing any existing behavior (dependent property notifications, collection locking, filter reentrancy guard, UI-thread marshalling, debounce, dispose).
- Remove the dead `Avalonia.ReactiveUI` dependency and `.UseReactiveUI()` call from the test project.
- Keep the existing public surface of `MainWindowViewModel` and `PluginListItem` (property names, method signatures) so XAML bindings, tests, `MainWindow.axaml.cs` code-behind, and `FormID Database Manager.TestUtilities` continue to compile unchanged where possible.
- Keep single-file trimmed publish green.

**Non-Goals:**

- No introduction of a DI container — `MainWindow.axaml.cs` keeps wiring services manually.
- No migration to `[RelayCommand]` at this time; the app has no `ICommand` properties in the view-model, so there is nothing to convert. If a future command is needed it can use `[RelayCommand]`, but this change does not force that.
- No refactor of services (`DatabaseService`, `ModProcessor`, `PluginProcessingService`, etc.) — they are MVVM-agnostic.
- No change to the four existing domain specs.
- No change to the `Mutagen/` submodule.
- No switch to AOT publish (`PublishAot=true`) — only trim/single-file is kept.

## Decisions

### 1. Pick Avalonia 12 target version

**Decision:** Pin every `Avalonia.*` package to the latest Avalonia 12 stable release available on NuGet at implementation time. Keep all Avalonia packages on an identical version string (they ship together). If only a pre-release is available when implementation starts, surface that in the tasks as a decision point for the implementer rather than silently shipping pre-release.

**Rationale:** Avalonia's own guidance is to keep every `Avalonia.*` package lockstep. Pinning an explicit version keeps reproducible builds.

**Alternatives considered:**

- Floating version (`12.*`) — rejected; reproducibility suffers and transitive-package mismatches become opaque.
- Staying on 11 and only doing the CTK swap — rejected; user explicitly asked for the 11→12 bump and it's the larger of the two risks, worth doing together so we only re-run the trim/publish matrix once.

### 2. Replace manual `INotifyPropertyChanged` with `ObservableObject`

**Decision:** Make `MainWindowViewModel : ObservableObject` (from `CommunityToolkit.Mvvm.ComponentModel`) and convert each simple field-backed property to `[ObservableProperty] private <T> _field;`. Delete the custom `SetProperty<T>` and `OnPropertyChanged([CallerMemberName])` overrides. Do the same for `Models/PluginListItem`.

**Rationale:** `ObservableObject` provides the exact same `SetProperty` / `OnPropertyChanged` semantics the hand-rolled helper was reproducing, but is source-generated per-property for `[ObservableProperty]`, eliminating dozens of lines of backing-field boilerplate. The generator is reflection-free, so trimming and single-file publish are unaffected.

**Alternatives considered:**

- Inherit from `ReactiveUI.ReactiveObject` and adopt `[Reactive]` / `WhenAny` — rejected; user explicitly chose CommunityToolkit.Mvvm, and it's a much lighter dependency with no Fody / extra scheduler story.
- Keep the hand-rolled `INotifyPropertyChanged` — rejected; defeats the stated goal.
- Use `C# 14` field-keyword partial-property style only — rejected; it does not emit `PropertyChanged` or handle `[NotifyPropertyChangedFor]` dependents, which we still need for `IsGameSelected`, `HasMultipleDirectories`, `IsProgressVisible`.

### 3. Express dependent ("computed") properties via `[NotifyPropertyChangedFor]`

**Decision:** For each computed property that the current view-model manually re-raises (`IsGameSelected`, `HasMultipleDirectories`, `IsProgressVisible`), annotate the underlying observable property with `[NotifyPropertyChangedFor(nameof(DependentProp))]`. Keep the computed property as a regular `public bool X => …` expression-bodied member. Example:

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsGameSelected))]
private GameRelease? _selectedGame;

public bool IsGameSelected => SelectedGame.HasValue;
```

For `HasMultipleDirectories`, which today is re-raised from `DetectedDirectories.CollectionChanged`, keep that subscription exactly as-is — `[NotifyPropertyChangedFor]` does not help with collection-change events, and the current `OnPropertyChanged(nameof(HasMultipleDirectories))` call continues to work because `ObservableObject` exposes `OnPropertyChanged(string)` with identical signature.

**Rationale:** Preserves observable public API and avoids hand-written getters/setters on the dependent flags.

**Alternatives considered:**

- Manually override `OnPropertyChanged` — rejected; `[NotifyPropertyChangedFor]` is the idiomatic CTK way and clearer at the declaration site.

### 4. Keep `PluginFilter` debounce logic but move it into a `partial void` hook

**Decision:** The `PluginFilter` setter currently calls `ApplyFilter()` (or `DebounceApplyFilter()`). With `[ObservableProperty]`, CTK generates the setter for us. Use the generated `partial void OnPluginFilterChanged(string value)` hook to invoke `ApplyFilter()` / `DebounceApplyFilter()` with the exact same conditional (`_debounceMs <= 0`). Debounce state (`_debounceCts`) and dispose logic stay as private fields / methods. Do not change the `Interlocked` reentrancy guard, `_filterSuspended` flag, or `_dispatcher.CheckAccess()` re-post logic inside `ApplyFilter`.

**Rationale:** Keeps existing thread-safety invariants intact while still benefiting from the generated setter.

**Alternatives considered:**

- Move the filter pipeline to `ReactiveUI` `WhenAnyValue(...).Throttle(...)` — rejected; drags in ReactiveUI, which is exactly what we're removing.
- Use `CommunityToolkit.Mvvm.Messaging` — rejected; overkill for a single intra-VM debounce.

### 5. Leave `LockedObservableCollection<T>`, `IThreadDispatcher`, and `Dispose()` as-is

**Decision:** Do not touch `LockedObservableCollection<T>` or `IThreadDispatcher` / `AvaloniaThreadDispatcher` / `SynchronousThreadDispatcher`. `MainWindowViewModel` will continue to `: IDisposable` for debounce cleanup. These types are orthogonal to MVVM.

**Rationale:** They solve Avalonia-thread + cross-thread mutation concerns that CTK doesn't address. Swapping them out would be an unrelated refactor.

### 6. Remove `Avalonia.ReactiveUI` from the test project and drop `.UseReactiveUI()`

**Decision:** Delete the `<PackageReference Include="Avalonia.ReactiveUI" ... />` line from `FormID Database Manager.Tests.csproj`. In `TestInitialization.cs`, remove `using Avalonia.ReactiveUI;` and the `.UseReactiveUI()` call from `BuildAvaloniaApp`. No test references `RxApp`, `ReactiveObject`, `WhenActivated`, or any scheduler-setup API, so nothing else needs to change.

**Rationale:** Dead dependency; every `using Avalonia.ReactiveUI` / `ReactiveUI` hit outside that one file lives in the `Mutagen/` submodule.

**Alternatives considered:**

- Leave it in place "just in case" — rejected; it's compile-time noise, adds to the trim-analyzer surface, and is an outright incompatible version pin on Avalonia 12 once the bump lands.

### 7. `PluginListItem` also uses CTK

**Decision:** `PluginListItem` becomes `partial class PluginListItem : ObservableObject, IDataErrorInfo` with `[ObservableProperty] private string _name = string.Empty;` and `[ObservableProperty] private bool _isSelected;`. `IDataErrorInfo` implementation (`Error`, `this[string]`) stays as written.

**Rationale:** Consistency with `MainWindowViewModel` and removes the same hand-rolled boilerplate. `IDataErrorInfo` is independent of observable-property generation.

### 8. CTK package and publish matrix

**Decision:** Add `<PackageReference Include="CommunityToolkit.Mvvm" Version="<latest-stable>" />` to the main project only (tests reach it transitively via `ProjectReference`). Do not add it to `FormID Database Manager.TestUtilities.csproj`. After the bump, run `dotnet publish "FormID Database Manager" -c Release -r win-x64` and verify:

1. Exit code is 0.
2. No new trim warnings above the existing baseline.
3. Resulting `.exe` launches and opens MainWindow.

If trim warnings appear from CTK, add `CommunityToolkit.Mvvm` to `<TrimmerRootAssembly Include=... />` alongside Mutagen as a fallback, but only if necessary.

**Rationale:** CTK is a source generator + a small runtime library. It should be fully trim-safe out of the box; `TrimmerRootAssembly` is a safety valve only.

### 9. Avalonia 12 API breakage strategy

**Decision:** Do the package bump first with the existing code and let the compiler tell us what broke. Fix exactly the surfaced callsites (most likely `AvaloniaTestApplication` attribute namespace, `AvaloniaHeadlessPlatformOptions`, or any `Dispatcher.UIThread` renames) and nothing else. Do not preemptively rewrite code "because Avalonia 12 does X" — keep the diff minimal.

**Rationale:** Smallest-possible-diff migration reduces regression surface. The main project is ~1.5k LOC of UI; API breaks should be tractable.

## Risks / Trade-offs

- **[Avalonia 12 not-yet-stable]** → Implementer must verify the latest stable release on NuGet before pinning. If only a pre-release is available, pause and confirm with the user before pinning a `-preview` / `-rc` tag. Task plan flags this explicitly.
- **[Silent AXAML breakage from Avalonia 12]** → XAML parser / compiled-binding rules can tighten between majors. Mitigation: run `dotnet build` after the bump and fix each `XAML1###` / `AVLN####` diagnostic individually before touching MVVM.
- **[`[NotifyPropertyChangedFor]` vs. ordering]** → CTK raises the dependent `PropertyChanged` *after* the main property's change event. Current code raises it inside `SetProperty` via a manual `OnPropertyChanged(nameof(IsGameSelected))` call also after the main one, so ordering is preserved. Mitigation: existing UI tests (`[AvaloniaFact]`) for VM should continue to pass; if any regress, fall back to manually raising inside the generated `OnXxxChanged` partial hook.
- **[`LockedObservableCollection` vs. CTK base class]** → `ObservableObject` does not change how we author `ObservableCollection<T>` subclasses. Nothing in CTK conflicts with our locked collection. Mitigation: verified by code inspection — no change needed.
- **[Trim warnings from CTK]** → CTK 8.x is source-generator-first and trim-clean in practice. Mitigation: if new warnings appear, add `CommunityToolkit.Mvvm` to `<TrimmerRootAssembly>`.
- **[Test-project Avalonia double-registration]** → Dropping `.UseReactiveUI()` means the scheduler defaults to Avalonia's own `AvaloniaScheduler`. No production test depends on `RxApp.MainThreadScheduler`, so this is safe. Mitigation: run the UI test collection after the change.
- **[`field` keyword in `PluginListItem`]** → The current file uses the C# `field` keyword (requires a C# 14 / net10 compiler). CTK's `[ObservableProperty]` replaces those auto-accessors with generator-emitted ones, so this concern evaporates after migration.
- **[.NET 10 + CTK version mismatch]** → CTK's package ships `netstandard2.0` plus modern TFMs; .NET 10 is supported. Mitigation: pin CTK to the current stable major (8.x at time of writing) — verify on NuGet before committing.

## Migration Plan

1. **Package bump (main + tests):** Update `Avalonia.*` to the Avalonia 12 stable line in both csproj files. Run `dotnet build "FormID Database Manager.slnx"`. Fix any compilation breaks in a single pass (small surface expected: `App.axaml.cs`, `Program.cs`, `TestInitialization.cs`, `MainWindow.axaml.cs`).
2. **Drop ReactiveUI (tests):** Remove `<PackageReference Include="Avalonia.ReactiveUI" />` and update `TestInitialization.cs` to drop the `using` + `.UseReactiveUI()` call.
3. **Add CTK (main):** Add `<PackageReference Include="CommunityToolkit.Mvvm" />` to the main csproj. Build.
4. **Convert `PluginListItem` first (smaller blast radius):** Mark the class `partial`, derive from `ObservableObject`, convert `Name` and `IsSelected` to `[ObservableProperty]`. Run the full test suite.
5. **Convert `MainWindowViewModel`:** In one pass, mark the class `partial`, derive from `ObservableObject`, convert each plain-state field to `[ObservableProperty]`, add `[NotifyPropertyChangedFor]` for `IsGameSelected` / `IsProgressVisible`, move `PluginFilter` side-effect into `OnPluginFilterChanged`. Delete the private `SetProperty<T>` and `OnPropertyChanged` helpers. Keep `Dispose`, `DebounceApplyFilter`, `ApplyFilter`, `AddErrorMessage`, `AddInformationMessage`, `UpdateProgress`, `ResetProgress`, `GetSelectedPlugins`, `SuspendFilter`, `ResumeFilter`, `LockedObservableCollection<T>` exactly as-is.
6. **Run full test suite:** `dotnet test "FormID Database Manager.Tests"`. Fix only real regressions.
7. **Publish smoke test:** `dotnet publish "FormID Database Manager" -c Release -r win-x64`. Launch resulting exe, pick a game directory, process one small plugin, confirm database produced.
8. **Docs:** Update `AGENTS.md` tech-stack bullets and testing-conventions line.

**Rollback:** `git revert` the commit(s). No data migration, no on-disk format change, no schema change. Users upgrading have nothing to undo.

## Open Questions

- **Which exact Avalonia 12.x patch is the current stable at implementation time?** The tasks file will list the version as a placeholder (`12.x.y`) and require the implementer to fill it in from `nuget.org` before committing.
- **Which exact CommunityToolkit.Mvvm version?** Same pattern — pin the latest stable `8.x` available at implementation time.
