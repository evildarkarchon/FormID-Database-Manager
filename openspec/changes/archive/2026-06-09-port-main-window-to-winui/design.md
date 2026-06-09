## Context

Phase 0 selected packaged MSIX, Phase 1 extracted the UI-neutral core, and Phase 2 added a packaged `FormID Database Manager.WinUI` shell that builds and launches. The WinUI project still contains the template `Frame` plus `Views/MainPage` counter UI, while the production workflow remains in Avalonia `MainWindow.axaml` and `MainWindow.axaml.cs`.

The Avalonia main window combines the visual workflow, service construction, picker calls, plugin-list commands, processing commands, and window lifetime cancellation. Phase 3 should move the visual surface and core ViewModel binding into WinUI without pulling in the Phase 4 picker/dispatcher service work or the Phase 5 full workflow-parity verification.

## Goals / Non-Goals

**Goals:**
- Replace the WinUI template counter UI with the FormID Database Manager main workflow surface.
- Introduce a WinUI `MainWindow` surface that binds to `MainWindowViewModel` and core model types.
- Port the Avalonia layout to WinUI-native controls, properties, resources, and visibility patterns.
- Keep the app on the standard WinUI title bar and packaged shell baseline.
- Preserve the existing Avalonia app while the staged WinUI port is incomplete.
- Build and launch the WinUI app far enough to manually verify the ported UI renders.

**Non-Goals:**
- Do not implement Windows App SDK file/folder pickers in this phase.
- Do not implement the final WinUI dispatcher service in this phase unless a minimal binding-safe adapter is required to render the UI.
- Do not remove Avalonia files, packages, converters, headless tests, or publish settings.
- Do not claim full processing workflow parity; Phase 5 owns end-to-end behavior verification.
- Do not add custom title bar behavior, Mica/Acrylic polish, or responsive redesign beyond what is needed for a usable initial port.

## Decisions

### Use a WinUI `MainWindow` as the production shell

Create a `MainWindow.xaml`/`MainWindow.xaml.cs` in the WinUI project and have `App.OnLaunched` instantiate that window directly. The current app is a single-window workflow, and a real window class is the clearest place to manage title, size, close-time cancellation, and later picker ownership.

Alternative considered: keep the generated `Frame` and replace `MainPage` content. That would preserve more template code, but it adds navigation structure the app does not need and makes later `AppWindow.Id` picker ownership less direct.

### Port layout behavior before visual polish

Translate the existing rows and control groups into WinUI `Grid`, `StackPanel`, `ListView`, `ComboBox`, `TextBox`, `CheckBox`, `ProgressBar`, and message controls. Keep sizing, labels, and workflow order familiar so behavior parity can be compared against the Avalonia baseline before visual redesign.

Alternative considered: redesign the layout while porting. That would make Phase 3 more satisfying visually, but it would blur migration regressions with intentional UX changes.

### Prefer WinUI-native visibility and message surfaces

Replace Avalonia `IsVisible` and `Boolean_Converter` usage with WinUI `Visibility`, `InfoBar`, or small WinUI-native message panels. If binding directly to collection counts is unreliable, add tight ViewModel properties such as `HasErrorMessages` and `HasInformationMessages` that raise when the corresponding collections change.

Alternative considered: add a direct clone of the Avalonia boolean converter. That would compile quickly, but explicit ViewModel state is easier to test and avoids binding to collection implementation details.

### Keep picker-dependent commands graceful

The Browse, Select Database, and Select List File controls should be present in the WinUI UI, but the Windows App SDK picker implementation remains Phase 4. Any Phase 3 event handlers for those buttons should avoid throwing and either remain unwired until Phase 4 or report that picker wiring is pending.

Alternative considered: implement pickers now. That would make the UI more usable immediately, but it would collapse Phase 4 into Phase 3 and increase the chance of confusing UI-port issues with AppWindow/picker API issues.

### Use `ListView` virtualization for plugins

Replace the Avalonia `ItemsControl`/`VirtualizingStackPanel` plugin list with a WinUI `ListView` using an item template with a `CheckBox` bound to `PluginListItem.IsSelected`. The plugin list is the largest dynamic UI surface, so it should stay virtualization-friendly from the first WinUI port.

Alternative considered: use a simple `ItemsRepeater` or `ItemsControl`. Those may be useful later, but `ListView` gives built-in selection, scrolling, and virtualization behavior with less custom code.

## Risks / Trade-offs

- WinUI binding semantics may differ from Avalonia -> Add explicit ViewModel properties or WinUI converters only where the port proves they are needed.
- The staged UI may expose controls whose platform services are not wired yet -> Keep Phase 3 verification focused on rendering and safe interactions, then route picker work through Phase 4.
- Replacing the template `MainPage` may disturb the known-good launch baseline -> Keep `App.OnLaunched` changes minimal and build/launch immediately after the new window is introduced.
- Message and progress controls may look different from the Avalonia baseline -> Prefer WinUI-native surfaces now, then use Phase 10 for visual polish and accessibility depth.
- Long plugin lists could regress responsiveness if virtualization is lost -> Use `ListView` and manually inspect scrolling behavior once sample plugin data is available.

## Migration Plan

1. Add a WinUI `MainWindow` and update `App.OnLaunched` to create it directly.
2. Remove or stop navigating to the template counter `MainPage` once `MainWindow` owns the UI.
3. Replace template sample resources in `App.xaml` with neutral, theme-aware resources needed by the port.
4. Translate the Avalonia main-window layout to WinUI XAML with standard controls and a standard title bar.
5. Bind controls to `MainWindowViewModel` and `PluginListItem`, adding minimal ViewModel binding-support properties if needed.
6. Add safe Phase 3 command handlers for non-picker controls and avoid throwing from picker-dependent buttons.
7. Build the WinUI project, build the solution, and launch the packaged WinUI app to inspect the ported UI.

Rollback strategy: revert the WinUI `App`, `MainWindow`, and resource edits and restore the template `MainPage` navigation. The existing Avalonia app remains untouched, so rollback does not affect the working production UI.

## Open Questions

- Whether Phase 4 should wire services directly in `MainWindow.xaml.cs` first or introduce a small composition root remains open.
- The exact WinUI picker APIs and fallback strategy remain Phase 4 work.
- Final title bar, backdrop, iconography, accessibility, and layout-density polish remain later migration phases.
