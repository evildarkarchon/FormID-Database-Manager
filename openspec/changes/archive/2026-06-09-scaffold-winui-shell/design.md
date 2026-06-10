## Context

Phase 0 selected packaged MSIX as the target deployment model, and Phase 1 moved reusable models, ViewModels, services, and abstractions into `FormID Database Manager.Core`. The current Avalonia app still owns startup, AXAML views, Avalonia threading, and picker implementations. Phase 2 needs to add a WinUI shell without porting the main UI yet, so later phases can work from a known-good Windows App SDK template baseline.

The local `dotnet new list winui` command exposes the C# WinUI templates. The installed `dotnet new winui` template supports `net10.0` and a `--unpackaged` option that currently defaults to `true`, so the packaged migration decision must be expressed explicitly when scaffolding.

## Goals / Non-Goals

**Goals:**
- Create a new `FormID Database Manager.WinUI` shell project from the WinUI template.
- Keep the shell additive and leave the existing Avalonia project buildable.
- Use the packaged MSIX model selected in Phase 0.
- Reference `FormID Database Manager.Core` from the WinUI shell.
- Add the shell to `FormID Database Manager.slnx`.
- Build the generated shell and verify that the blank app launches before UI porting starts.
- Record any local WinUI deployment or launch constraints discovered during verification.

**Non-Goals:**
- Do not port `MainWindow.axaml` or recreate the production UI in this phase.
- Do not implement WinUI picker or dispatcher services yet.
- Do not remove Avalonia packages, AXAML files, headless tests, or the current publish settings.
- Do not change Mutagen parsing, SQLite writing, plugin filtering, progress, cancellation, or database output behavior.

## Decisions

### Scaffold a separate WinUI project

Create `FormID Database Manager.WinUI` beside the existing Avalonia project and add it to the current `.slnx`. This follows the staged migration plan: the current app remains the working product while the WinUI shell proves startup and packaging.

Alternative considered: convert the existing `FormID Database Manager` project directly to Windows App SDK. That would combine template setup, Avalonia removal, and UI porting in one hard-to-debug step.

### Use the template-first packaged baseline

Run the WinUI template with `--unpackaged false` and `--no-solution-file`, targeting `net10.0` unless the template or SDK requires a more specific generated Windows TFM. The `--no-solution-file` option avoids creating an extra solution because the repo already has `FormID Database Manager.slnx`.

Alternative considered: accept the installed template default. That would currently create an unpackaged app and conflict with the Phase 0 packaged MSIX decision.

### Keep the WinUI shell minimal

Retain the generated `App.xaml`, `App.xaml.cs`, package manifest, assets, and starter page/window files with only the edits needed for project naming, solution membership, and the core project reference. The first successful shell should look close to the generated template so startup failures can be compared against the template baseline.

Alternative considered: immediately reshape the shell into the final application structure. That belongs in later phases after blank startup and package deployment are known to work.

### Reference core without wiring production ViewModels yet

Add a `ProjectReference` to `FormID Database Manager.Core` and build with that dependency, but do not bind the generated page to `MainWindowViewModel` yet. Referencing core proves the architectural dependency and package compatibility while avoiding premature UI behavior porting.

Alternative considered: wire the full ViewModel immediately. That would pull Phase 3 and Phase 4 work into the shell baseline and make startup failures harder to isolate.

## Risks / Trade-offs

- Installed template defaults to unpackaged -> Pass `--unpackaged false` and verify `Package.appxmanifest` plus packaged project properties exist after scaffolding.
- Packaged WinUI launch may require Visual Studio deploy or Developer Mode -> Verify the blank shell through the selected local workflow and document the exact launch path or blocker.
- Windows App SDK package versions may conflict with existing dependencies -> Keep Mutagen and SQLite in core, add only template-required WinUI packages to the shell, and build the full solution after the reference is added.
- The shell may generate extra files or solution artifacts -> Use `--no-solution-file`, review generated files, and add only the intended `.csproj` to `FormID Database Manager.slnx`.
- Later phases may need a different app identity or signing setup -> Keep package identity/template manifest defaults minimal in Phase 2 and defer distribution decisions to Phase 9.

## Migration Plan

1. Confirm the WinUI template is still installed with `dotnet new list winui`.
2. Dry-run the scaffold command to confirm generated file paths.
3. Create `FormID Database Manager.WinUI` with the packaged WinUI template and no generated solution file.
4. Add a `ProjectReference` from the WinUI project to `FormID Database Manager.Core`.
5. Add the WinUI project to `FormID Database Manager.slnx`.
6. Build the new WinUI project and the full solution.
7. Launch the blank WinUI app through the appropriate packaged local workflow and record objective evidence or the environment blocker.
8. Update OpenSpec tasks with the verification results.

Rollback strategy: remove `FormID Database Manager.WinUI` from the solution and delete the generated shell directory. Because the existing Avalonia app is untouched, rollback does not require moving core files or restoring Avalonia behavior.

## Open Questions

- Which exact package identity, publisher, display name, and signing approach should be used for release builds remains a Phase 9 deployment decision.
- Whether future implementation should keep a generated starter page or move directly to a single `MainWindow.xaml` shell will be decided in Phase 3 when the production UI is ported.
