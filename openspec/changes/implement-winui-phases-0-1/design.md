## Context

The current application is an Avalonia desktop app that also contains reusable Mutagen, SQLite, game detection, load-order, ViewModel, and model code in the UI project. The WinUI migration plan calls for a template-first WinUI migration, but phases 0 and 1 deliberately stop before creating the WinUI shell. The immediate need is to preserve existing behavior while making later WinUI work reference a UI-neutral core.

The repo is already on a `winui` branch. The WinUI template is installed (`dotnet new list winui` shows the C# WinUI template), and current Microsoft guidance uses the packaged WinUI template as the first-app path.

## Goals / Non-Goals

**Goals:**
- Record the Phase 0 baseline and toolchain status before moving code.
- Document the selected deployment model as packaged MSIX for the staged WinUI migration.
- Add a `.NET 10` core class library that owns UI-neutral models, ViewModels, services, and abstractions.
- Leave Avalonia-only files and implementations in the existing application project.
- Keep current processing behavior and tests compiling while project references move toward the core boundary.

**Non-Goals:**
- Do not scaffold or port the WinUI shell in this change.
- Do not remove Avalonia packages or headless UI tests yet.
- Do not rewrite processing, filtering, database schema, Mutagen parsing, or load-order behavior.
- Do not change the current publish output for the Avalonia app.

## Decisions

### Use a staged core project instead of converting the Avalonia project
The new `FormID Database Manager.Core` project will contain UI-neutral code and will be referenced by the existing Avalonia app, tests, and test utilities. This keeps the current app buildable while later phases add a WinUI project.

Alternative considered: directly convert the current app project into a Windows App SDK project. That would combine UI porting, package changes, and business-logic movement in one riskier step.

### Keep Avalonia platform services in the app project
`AvaloniaThreadDispatcher` and `WindowManager` stay in the app project because they depend on Avalonia threading and storage picker APIs. Core exposes the cross-platform contracts that the current Avalonia project and future WinUI project can implement.

Alternative considered: move the current Avalonia implementations to core temporarily. That would preserve less file movement now but would make the new core project fail its UI-neutral purpose.

### Add `IFileDialogService` before the WinUI picker implementation
The ViewModel-facing picker contract is introduced in core during Phase 1, even though the current Avalonia `WindowManager` remains in the UI project. Later phases can implement the same contract with Windows App SDK picker APIs.

Alternative considered: wait until Phase 4 to add the abstraction. Introducing it now makes the core boundary explicit and easier to test as ViewModel code is prepared for WinUI.

### Target `.NET 10` for core
The core library uses `net10.0` to match the existing solution and avoid introducing multi-targeting while the migration is staged.

Alternative considered: target `netstandard` or a Windows-specific TFM. `netstandard` is unnecessary for this Windows-only migration, and a Windows-specific TFM would weaken the UI-neutral boundary.

## Risks / Trade-offs

- Project-reference churn can break tests unexpectedly -> keep the first extraction mechanical and run the full solution build/test after each meaningful move.
- Namespace preservation may make the physical project boundary less obvious -> keep existing namespaces initially to minimize behavior and test changes, then revisit naming only after WinUI parity.
- UI-neutral services may still have hidden Avalonia references -> search for `Avalonia`, `axaml`, and platform-specific picker/threading APIs after extraction.
- Introducing `IFileDialogService` without fully rewiring the ViewModel may look incomplete -> keep the abstraction present now, but defer UI call-site rewiring if it would expand Phase 1 beyond the migration plan.

## Migration Plan

1. Record Phase 0 baseline results and WinUI template status.
2. Create `FormID Database Manager.Core` and add it to the solution.
3. Move UI-neutral models, ViewModels, and services into core.
4. Leave Avalonia startup, views, converters, dispatcher implementation, and picker implementation in the app project.
5. Update project references so the app, tests, and test utilities consume the core project.
6. Add README migration notes for packaged MSIX as the selected target deployment model.
7. Build and test the solution.

Rollback strategy: because the extraction is staged and the Avalonia project remains, rollback can remove the core project from the solution and move the files back to their original directories without needing to undo a WinUI shell conversion.

## Open Questions

- The long-term package signing, distribution channel, and app identity are still Phase 9 decisions.
- Automated WinUI UI testing depth remains a later decision after the shell exists.
