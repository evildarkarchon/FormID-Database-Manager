## ADDED Requirements

### Requirement: MVVM backend

The main project (`FormID Database Manager`) SHALL use `CommunityToolkit.Mvvm` as its sole MVVM backend. The project SHALL reference the `CommunityToolkit.Mvvm` NuGet package. The project SHALL NOT reference `ReactiveUI`, `Avalonia.ReactiveUI`, `ReactiveUI.Fody`, or any other `ReactiveUI.*` package. All `INotifyPropertyChanged` implementations in the project SHALL derive from `CommunityToolkit.Mvvm.ComponentModel.ObservableObject` rather than implementing `INotifyPropertyChanged` manually.

#### Scenario: Main project references CommunityToolkit.Mvvm

- **WHEN** `FormID Database Manager/FormID Database Manager.csproj` is inspected
- **THEN** it contains exactly one `<PackageReference Include="CommunityToolkit.Mvvm" ... />` element with a pinned (non-floating) version

#### Scenario: Main project does not reference ReactiveUI

- **WHEN** any `.csproj` under `FormID Database Manager/` (excluding the `Mutagen/` submodule) is inspected
- **THEN** it contains no `<PackageReference>` whose `Include` starts with `ReactiveUI` or equals `Avalonia.ReactiveUI`

#### Scenario: View-models inherit from ObservableObject

- **WHEN** any non-abstract class under `FormID Database Manager/ViewModels/` or `FormID Database Manager/Models/` that needs `INotifyPropertyChanged` is inspected
- **THEN** it inherits (directly or indirectly) from `CommunityToolkit.Mvvm.ComponentModel.ObservableObject`
- **AND** it does not implement `System.ComponentModel.INotifyPropertyChanged` manually
- **AND** it does not define its own `protected bool SetProperty<T>(ref T, T, string)` helper

---

### Requirement: Avalonia 12 package versions

All `Avalonia.*` package references in `FormID Database Manager.csproj` and `FormID Database Manager.Tests.csproj` SHALL resolve to the same Avalonia 12 stable version. No `Avalonia.*` package in either project SHALL remain on an Avalonia 11.x version after this change is applied.

#### Scenario: Main project uses Avalonia 12

- **WHEN** `FormID Database Manager/FormID Database Manager.csproj` is inspected
- **THEN** every `<PackageReference>` whose `Include` starts with `Avalonia` has a `Version` that starts with `12.`
- **AND** all such `Version` values are identical

#### Scenario: Tests project uses Avalonia 12

- **WHEN** `FormID Database Manager.Tests/FormID Database Manager.Tests.csproj` is inspected
- **THEN** every `<PackageReference>` whose `Include` starts with `Avalonia` has a `Version` that starts with `12.`
- **AND** all such `Version` values are identical to those in the main csproj

#### Scenario: Test bootstrapper does not configure ReactiveUI

- **WHEN** `FormID Database Manager.Tests/TestInitialization.cs` is inspected
- **THEN** it does not contain `using Avalonia.ReactiveUI`
- **AND** its `BuildAvaloniaApp` method does not call `.UseReactiveUI()`

---

### Requirement: Observable properties via source generators

Simple field-backed observable state on `ObservableObject`-derived classes SHALL be declared with the `[ObservableProperty]` attribute rather than hand-written backing fields plus property setters that call `SetProperty`. Properties whose notifications must also raise `PropertyChanged` for one or more computed ("dependent") properties SHALL use `[NotifyPropertyChangedFor(nameof(...))]` on the field declaration. The existing public property names on `MainWindowViewModel` and `PluginListItem` SHALL be preserved so that existing XAML bindings continue to resolve without modification.

#### Scenario: Simple observable property is generator-emitted

- **WHEN** `MainWindowViewModel.GameDirectory` (or any equivalently simple observable property) is inspected
- **THEN** its implementation is a source-generated property backed by a `[ObservableProperty]`-attributed private field
- **AND** no hand-written setter that calls a `SetProperty` helper exists for it

#### Scenario: Dependent property is declared with NotifyPropertyChangedFor

- **WHEN** `MainWindowViewModel.SelectedGame` is set to a new value that changes `IsGameSelected`
- **THEN** the view-model raises `PropertyChanged` for both `SelectedGame` and `IsGameSelected`
- **AND** this behavior is declared via `[NotifyPropertyChangedFor(nameof(IsGameSelected))]` on the `SelectedGame` backing field

#### Scenario: Public binding surface is unchanged

- **WHEN** `MainWindow.axaml` is inspected for `{Binding ...}` and compiled-binding expressions targeting `MainWindowViewModel`
- **THEN** every binding path that existed before this change resolves to a public property of the same name on the migrated view-model

---

### Requirement: Thread-safety and lifecycle behavior preserved

The migration to `CommunityToolkit.Mvvm` SHALL NOT remove or regress any of the following thread-safety and lifecycle mechanisms on `MainWindowViewModel`: the `IThreadDispatcher` abstraction and its UI-thread `CheckAccess` / `Post` usage, the `LockedObservableCollection<T>` nested class, the `_pluginsLock` and `_messagesLock` locks, the `Interlocked.CompareExchange`-based filter reentrancy guard, the `_filterSuspended` flag with `SuspendFilter` / `ResumeFilter`, the `PluginFilter` debounce via `CancellationTokenSource` + `Task.Delay(_debounceMs, ...)`, the `IDisposable` implementation that cancels and disposes the debounce CTS, and the `DetectedDirectories.CollectionChanged` subscription that re-raises `HasMultipleDirectories`.

#### Scenario: UI-thread marshalling is preserved

- **WHEN** `AddErrorMessage`, `AddInformationMessage`, `UpdateProgress`, `ResetProgress`, or `ApplyFilter` is called from a non-UI thread
- **THEN** it re-posts itself via `_dispatcher.Post(...)` before mutating observable state

#### Scenario: Filter debounce is preserved

- **WHEN** `MainWindowViewModel` is constructed with `debounceMs > 0` and `PluginFilter` is assigned
- **THEN** `ApplyFilter` is not invoked synchronously from the setter
- **AND** it is scheduled after approximately `debounceMs` milliseconds via the existing `CancellationTokenSource` + `Task.Delay` pipeline

#### Scenario: Filter reentrancy guard is preserved

- **WHEN** `ApplyFilter` is called and is already running on another stack frame
- **THEN** the second invocation returns immediately without mutating `FilteredPlugins`
- **AND** this is implemented via `Interlocked.CompareExchange(ref _isApplyingFilter, 1, 0)`

#### Scenario: Dispose cancels pending debounce

- **WHEN** `MainWindowViewModel.Dispose()` is called while a debounced filter is pending
- **THEN** the pending `Task.Delay` is cancelled and the `CancellationTokenSource` is disposed

#### Scenario: Plugins collection locking is preserved

- **WHEN** items are inserted, removed, moved, set, or cleared on `MainWindowViewModel.Plugins`
- **THEN** the mutation is wrapped by the `_pluginsLock` via `LockedObservableCollection<T>`

---

### Requirement: Trimmed single-file publish remains green

After this change is applied, `dotnet publish "FormID Database Manager" -c Release -r win-x64` SHALL succeed with the same properties the project already sets (`PublishTrimmed=true`, `TrimMode=partial`, `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`). The set of `<TrimmerRootAssembly>` entries SHALL remain the same unless trim warnings from `CommunityToolkit.Mvvm` require adding it as a root, in which case the addition is explicit and documented in-csproj.

#### Scenario: Release publish succeeds

- **WHEN** `dotnet publish "FormID Database Manager" -c Release -r win-x64` is run on a clean checkout with the change applied
- **THEN** the command exits with code 0
- **AND** it produces a self-contained single-file executable

#### Scenario: Trim root assemblies list is intentional

- **WHEN** `FormID Database Manager.csproj` is inspected
- **THEN** the `<TrimmerRootAssembly>` list includes the four `Mutagen.Bethesda.*` entries that already exist
- **AND** it includes `CommunityToolkit.Mvvm` only if doing so is required to eliminate a trim warning introduced by the migration
