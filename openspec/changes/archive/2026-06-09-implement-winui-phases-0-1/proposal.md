## Why

The WinUI migration needs a stable foundation before the Avalonia UI is replaced. Establishing a baseline and extracting UI-neutral code first keeps Mutagen/SQLite behavior testable while later phases port the Windows-specific shell.

## What Changes

- Record the current build, test, branch, WinUI template, and deployment model decisions for Phase 0.
- Add a `.NET 10` core class library for UI-neutral models, ViewModels, and services.
- Move processing, game detection, database, and load-order logic into the core project without changing behavior.
- Keep Avalonia-specific startup, views, converters, thread dispatching, and picker code in the existing UI project.
- Add an `IFileDialogService` abstraction in core so Avalonia and future WinUI picker implementations can sit behind the same ViewModel-facing contract.
- Update test and test-utility project references so UI-independent tests compile against the core project.
- Document the staged migration and selected deployment model in `README.md`.

## Capabilities

### New Capabilities
- `winui-migration-foundation`: Covers the Phase 0 baseline record, selected deployment model, and Phase 1 UI-neutral core boundary required for the WinUI migration.

### Modified Capabilities

## Impact

- Affected projects: `FormID Database Manager`, `FormID Database Manager.Core`, `FormID Database Manager.Tests`, and `FormID Database Manager.TestUtilities`.
- Affected code: `Models`, `ViewModels`, UI-neutral services, dispatcher/file-dialog abstractions, project references, solution membership, and README migration documentation.
- External behavior should remain unchanged for current Avalonia workflows; later phases will add the WinUI shell.
