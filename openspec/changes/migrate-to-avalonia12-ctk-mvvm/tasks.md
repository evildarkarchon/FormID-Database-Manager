## 1. Prep & version pinning

Implementation notes:

- Pinned Avalonia stable: `12.0.1`.
- Pinned CommunityToolkit.Mvvm stable: `8.4.2`.
- Baseline build: passed with 6 NU1903 warnings for `Tmds.DBus.Protocol` 0.21.2.
- Baseline tests: passed, 274 passed / 11 skipped / 285 total, with the same NU1903 warnings.
- Avalonia 12 has no `Avalonia.Diagnostics` package (`NU1102`, nearest `11.3.14`), so the legacy package was replaced with `AvaloniaUI.DiagnosticsSupport` `2.2.1` and DevTools wiring was updated.
- `Avalonia.Headless.XUnit` 12 required aligning test projects to xUnit v3; existing unrelated xUnit1051 cancellation-token analyzer warnings are suppressed in test project files.
- Machine validation passed: clean build 0 warnings, tests 274 passed / 11 skipped / 285 total, strict OpenSpec validation valid, and publish produced `FormID Database Manager.exe`.
- Interactive smoke tests 2.5, 7.3, and 8.3 were not run in this automated session and are deferred to a human with a game installation.

- [x] 1.1 Check nuget.org for the current **Avalonia 12 stable** release and record the exact `Major.Minor.Patch` (e.g., `12.0.0`). If no stable 12.x is published yet, pause and ask the user whether to pin the latest `-preview` / `-rc` tag before proceeding.
- [x] 1.2 Check nuget.org for the current **CommunityToolkit.Mvvm** stable version (expected to be in the `8.x` line) and record the exact version string.
- [x] 1.3 Take a baseline of the current build: run `dotnet build "FormID Database Manager.slnx"` and `dotnet test "FormID Database Manager.Tests"` on a clean tree and note the warning count and test pass count for post-change comparison.

## 2. Bump Avalonia to 12.x in the main project

- [x] 2.1 In `FormID Database Manager/FormID Database Manager.csproj`, update every `Avalonia.*` `<PackageReference>` `Version` (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics`) to the pinned Avalonia 12 version from task 1.1.
- [x] 2.2 Run `dotnet build "FormID Database Manager.slnx"` and record every compile error / warning introduced by the bump.
- [x] 2.3 Fix surfaced breakage in `FormID Database Manager/App.axaml`, `App.axaml.cs`, `Program.cs`, `MainWindow.axaml`, and `MainWindow.axaml.cs` one-by-one. Do **not** make speculative changes — only touch callsites the compiler flags.
- [x] 2.4 Re-run `dotnet build "FormID Database Manager.slnx"` and confirm it succeeds with no new warnings above the baseline from 1.3.
- [ ] 2.5 Manually launch the app (`dotnet run --project "FormID Database Manager"`) and confirm MainWindow opens and basic interactions work (browse game directory, select a plugin). Close the app.

## 3. Bump Avalonia to 12.x in the tests project and drop ReactiveUI

- [x] 3.1 In `FormID Database Manager.Tests/FormID Database Manager.Tests.csproj`, update every `Avalonia.*` `<PackageReference>` `Version` (`Avalonia`, `Avalonia.Themes.Fluent`, `Avalonia.Headless.XUnit`) to match the version pinned in 2.1.
- [x] 3.2 In the same csproj, **delete** the `<PackageReference Include="Avalonia.ReactiveUI" Version="11.3.9" />` line.
- [x] 3.3 In `FormID Database Manager.Tests/TestInitialization.cs`, delete `using Avalonia.ReactiveUI;` and remove the `.UseReactiveUI()` chained call from `BuildAvaloniaApp`.
- [x] 3.4 Run `dotnet test "FormID Database Manager.Tests" --no-build` and then `dotnet test "FormID Database Manager.Tests"`. Fix any Avalonia 12 API breakage in test code (expected touch points: `[AvaloniaFact]` namespace, headless options ctor, dispatcher helpers). Touch only what the compiler / test runner flags.
- [x] 3.5 Confirm UI-test collection (`Collection("UITests")` or equivalent) still runs serially and passes.

## 4. Add CommunityToolkit.Mvvm

- [x] 4.1 In `FormID Database Manager/FormID Database Manager.csproj`, add `<PackageReference Include="CommunityToolkit.Mvvm" Version="<pinned-8.x>" />` using the version recorded in 1.2.
- [x] 4.2 Run `dotnet build "FormID Database Manager.slnx"` and confirm the build still succeeds with no new warnings. No code changes yet — this step only proves the package restores clean alongside Avalonia 12.

## 5. Convert `Models/PluginListItem` to `ObservableObject`

- [x] 5.1 Edit `FormID Database Manager/Models/PluginListItem.cs`:
  - Mark the class `partial`.
  - Change the base list to `: ObservableObject, IDataErrorInfo` (add `using CommunityToolkit.Mvvm.ComponentModel;`).
  - Replace the `Name` `field`-keyword property with `[ObservableProperty] private string _name = string.Empty;`.
  - Replace the `IsSelected` `field`-keyword property with `[ObservableProperty] private bool _isSelected;`.
  - Delete the manual `PropertyChanged` event, the `OnPropertyChanged` method, and the `using System.ComponentModel;` / `System.Runtime.CompilerServices;` usings if they are no longer referenced.
  - Keep the `IDataErrorInfo.Error` property and the `this[string columnName]` indexer unchanged.
- [x] 5.2 Run `dotnet build "FormID Database Manager.slnx"` and resolve any compile issues.
- [x] 5.3 Run the full test suite: `dotnet test "FormID Database Manager.Tests"`. All `PluginListItem`-touching tests must pass.

## 6. Convert `ViewModels/MainWindowViewModel` to `ObservableObject`

- [x] 6.1 Edit `FormID Database Manager/ViewModels/MainWindowViewModel.cs` — **class declaration + usings**:
  - Mark the class `partial`.
  - Change the base list to `: ObservableObject, IDisposable` (add `using CommunityToolkit.Mvvm.ComponentModel;`).
  - Delete `using System.ComponentModel;` if no longer referenced.
- [x] 6.2 Convert each simple field-backed observable property to `[ObservableProperty]`, keeping the existing field names (prefix-`_`) and types:
  - `_databasePath` / `DatabasePath`
  - `_formIdListPath` / `FormIdListPath`
  - `_gameDirectory` / `GameDirectory`
  - `_progressStatus` / `ProgressStatus`
  - `_progressValue` / `ProgressValue`
  - `_pluginFilter` / `PluginFilter`
  - `_detectedDirectories` / `DetectedDirectories`
  - `_errorMessages` / `ErrorMessages`
  - `_informationMessages` / `InformationMessages`
  - `_filteredPlugins` / `FilteredPlugins` (keep the setter `private` by emitting a manual wrapper if the generator doesn't support that; otherwise expose via `[ObservableProperty]` and document in a comment).
- [x] 6.3 Convert observable properties that need dependent-property notifications by adding `[NotifyPropertyChangedFor]`:
  - `_selectedGame` → `[NotifyPropertyChangedFor(nameof(IsGameSelected))]`
  - `_isProcessing` → `[NotifyPropertyChangedFor(nameof(IsProgressVisible))]`
  - `_isScanning` → `[NotifyPropertyChangedFor(nameof(IsProgressVisible))]`
- [x] 6.4 Keep `IsGameSelected`, `HasMultipleDirectories`, and `IsProgressVisible` as expression-bodied getter properties (`public bool IsGameSelected => SelectedGame.HasValue;` etc.). Do **not** convert them to `[ObservableProperty]`.
- [x] 6.5 Keep the `DetectedDirectories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMultipleDirectories));` subscription in the constructor. `ObservableObject` already exposes an `OnPropertyChanged(string?)` override with matching signature.
- [x] 6.6 For `PluginFilter`, move the current setter side-effect into a new `partial void OnPluginFilterChanged(string value)` method, preserving the existing branch:
  ```csharp
  partial void OnPluginFilterChanged(string value)
  {
      if (_debounceMs <= 0) ApplyFilter();
      else DebounceApplyFilter();
  }
  ```
- [x] 6.7 For the custom `Plugins` property (which wraps the collection in `LockedObservableCollection<T>`), keep it as a **manually written** property (not `[ObservableProperty]`) so the existing normalization / event-subscription logic in the setter is preserved. Use `ObservableObject.SetProperty(ref _plugins, normalizedPlugins)` in place of the private helper call.
- [x] 6.8 Delete the private `SetProperty<T>` helper, the `OnPropertyChanged` override, and the `public event PropertyChangedEventHandler? PropertyChanged;` declaration — all of these are now provided by `ObservableObject`.
- [x] 6.9 Leave `ApplyFilter`, `DebounceApplyFilter`, `SuspendFilter`, `ResumeFilter`, `AddErrorMessage`, `AddInformationMessage`, `ResetProgress`, `UpdateProgress`, `GetSelectedPlugins`, `Dispose`, the `_messagesLock`, `_pluginsLock`, `_debounceCts`, `_isApplyingFilter`, `_filterSuspended`, and the nested `LockedObservableCollection<T>` class exactly as they are.
- [x] 6.10 Run `dotnet build "FormID Database Manager.slnx"` and resolve any remaining compile issues.

## 7. Verify behavior

- [x] 7.1 Run the full test suite: `dotnet test "FormID Database Manager.Tests"`. All unit, UI (`[AvaloniaFact]`), and integration tests that were passing before must still pass.
- [x] 7.2 If any test fails, confirm it is a real regression (not a stale snapshot) and fix the underlying view-model logic — **not** the test — unless the test was asserting on the internals of the deleted `SetProperty` helper.
- [ ] 7.3 Manually launch `dotnet run --project "FormID Database Manager"`, pick a small game directory, select one plugin, run the processing pipeline end-to-end, and confirm the output database contains rows. Close the app.

## 8. Publish smoke test

- [x] 8.1 Run `dotnet publish "FormID Database Manager" -c Release -r win-x64`. Confirm exit code 0.
- [x] 8.2 Inspect the output folder and confirm a self-contained single-file exe was produced.
- [ ] 8.3 Launch the published exe and confirm MainWindow opens. Close.
- [x] 8.4 If step 8.1 surfaced new trim warnings attributable to `CommunityToolkit.Mvvm`, add `<TrimmerRootAssembly Include="CommunityToolkit.Mvvm" />` to the existing `<ItemGroup>` that already roots the Mutagen assemblies, and re-run 8.1–8.3. Add an in-csproj comment explaining why.

## 9. Docs & cleanup

- [x] 9.1 Update `AGENTS.md` "Tech Stack" section: change the Avalonia UI bullet from `11.3` to `12.x` and append `CommunityToolkit.Mvvm` as a separate bullet.
- [x] 9.2 Update `AGENTS.md` testing-conventions or "Important Notes" wording to remove any reference to ReactiveUI if present.
- [x] 9.3 Grep the repo (excluding `Mutagen/` and `docs/mutagen/`) for leftover `ReactiveUI` / `Avalonia.ReactiveUI` strings. There must be zero hits in first-party code after this change.
- [x] 9.4 Grep the repo (excluding `Mutagen/` and `docs/mutagen/`) for `SetProperty<` and `INotifyPropertyChanged`. Confirm the only remaining hits are either inside `CommunityToolkit.Mvvm` comments, inside `IDataErrorInfo` or nested helper types that intentionally stay hand-written, or transitive references via Avalonia itself.

## 10. Final validation

- [x] 10.1 Run `openspec validate "migrate-to-avalonia12-ctk-mvvm" --strict`. It must report the change as valid.
- [x] 10.2 Run `dotnet build "FormID Database Manager.slnx"` and `dotnet test "FormID Database Manager.Tests"` one last time on a clean `bin/obj` and confirm both are green.
