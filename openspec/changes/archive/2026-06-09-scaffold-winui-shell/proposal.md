## Why

Phase 1 created a UI-neutral core, but the migration still lacks a real WinUI project that proves the Windows App SDK shell can coexist with the current solution. Phase 2 should establish a template-first WinUI baseline before any Avalonia UI is ported, giving later phases a known-good startup, packaging, and project-reference foundation.

## What Changes

- Scaffold a new C# WinUI shell project alongside the existing Avalonia app.
- Keep the selected packaged MSIX deployment model from Phase 0 unless a later change explicitly revisits deployment.
- Target the Windows-specific .NET TFM emitted by the WinUI template, such as `net10.0-windows10.0.19041.0`.
- Add the WinUI shell project to `FormID Database Manager.slnx`.
- Reference `FormID Database Manager.Core` from the new WinUI shell.
- Preserve the current Avalonia application and tests during this phase.
- Build and launch the blank WinUI app before any main-window UI porting begins.
- Record any WinUI toolchain or launch constraints discovered while creating the shell.

## Capabilities

### New Capabilities

### Modified Capabilities
- `winui-migration-foundation`: Adds Phase 2 requirements for a template-first WinUI shell project, core project reference, packaged baseline, and blank-shell build/launch verification.

## Impact

- Affected projects: a new WinUI shell project, `FormID Database Manager.Core`, and `FormID Database Manager.slnx`.
- Affected dependencies: Windows App SDK packages provided by the WinUI template, plus any template-required packaging assets.
- Affected workflows: local WinUI environment validation, solution build, and blank-shell launch verification.
- Existing Avalonia UI behavior and Mutagen/SQLite processing behavior should remain unchanged in this phase.
