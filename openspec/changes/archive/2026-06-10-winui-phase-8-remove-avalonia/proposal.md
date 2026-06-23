## Why

Phase 7 moved automated coverage away from Avalonia-specific test infrastructure, leaving the legacy Avalonia application as migration scaffolding rather than the target runtime. Phase 8 removes that scaffolding so the solution has one supported desktop shell, the WinUI app, and no dormant Avalonia dependencies to maintain.

## What Changes

- **BREAKING**: Remove the legacy Avalonia application project, startup path, AXAML views, Avalonia platform services, converters, and package references from the buildable application surface.
- Keep `FormID Database Manager.Core` as the UI-neutral owner of models, ViewModels, processing services, database behavior, game detection, load-order behavior, dispatching abstractions, and file-dialog abstractions.
- Keep `FormID Database Manager.WinUI` as the supported desktop application project and align solution/build documentation around the WinUI startup path.
- Remove any remaining automated test, helper, or package dependency on Avalonia, `Avalonia.Headless`, AXAML, or `[AvaloniaFact]`.
- Search for remaining Avalonia references and either remove them or document intentionally historical references in migration docs/archive material.
- Build the WinUI project, build the solution, run the automated test suite, and record Phase 8 verification results.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `winui-migration-foundation`: extend the staged WinUI migration contract with Phase 8 requirements for removing the legacy Avalonia app and dependencies while preserving core behavior, WinUI startup, tests, and verification.

## Impact

- Affected code: solution/project files, the legacy `FormID Database Manager` Avalonia project, Avalonia-specific source files, remaining test references, documentation build/run commands, and WinUI/core project references as needed.
- Affected behavior: local development and CI should treat `FormID Database Manager.WinUI` as the supported desktop shell; UI-neutral behavior remains owned by `FormID Database Manager.Core`.
- Dependencies: removes Avalonia runtime and test packages once no buildable project depends on them; no new runtime dependencies are expected.
