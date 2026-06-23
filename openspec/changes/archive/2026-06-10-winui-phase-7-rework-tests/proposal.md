## Why

Phase 6 stabilized WinUI binding and ViewModel behavior, so the next migration risk is that the automated test suite still depends on Avalonia headless infrastructure and the old app project for behavior that now lives in the core and WinUI layers. Phase 7 reworks the tests before Avalonia removal so later phases can remove UI-framework dependencies without losing coverage.

## What Changes

- Move service and ViewModel tests that validate UI-neutral behavior onto the `FormID Database Manager.Core` dependency path and keep them runnable with standard xUnit facts where no UI runtime is required.
- Convert ViewModel state-change tests from `AvaloniaFact` to regular xUnit facts backed by `SynchronousThreadDispatcher` or other UI-neutral test dispatchers.
- Replace Avalonia-specific dispatcher, startup, window, and picker tests with WinUI source/smoke coverage or tests around mockable core abstractions such as `IFileDialogService`.
- Retire `Avalonia.Headless.XUnit`, `TestInitialization.cs`, and `UiTestHost` after equivalent or better WinUI/core coverage is in place.
- Keep integration, performance, load, stress, and regression tests focused on Mutagen, SQLite, processing, and game-environment behavior rather than UI-framework mechanics.
- Add a Windows CI workflow that builds the solution and runs the automated tests on a Windows runner.
- Record Phase 7 verification results in durable change verification notes.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `winui-migration-foundation`: extend the staged WinUI migration contract with Phase 7 requirements for core-first test coverage, replacement of Avalonia headless test infrastructure, WinUI/platform-service test coverage, Windows CI, and verification.

## Impact

- Affected code: `FormID Database Manager.Tests`, `FormID Database Manager.TestUtilities`, `FormID Database Manager.Tests.csproj`, WinUI/platform-service source tests, existing UI test files, and `.github/workflows` if no suitable Windows test workflow exists.
- Affected behavior: automated verification becomes less dependent on Avalonia and better aligned with the shared core plus WinUI shell migration path.
- Dependencies: removes Avalonia headless test dependencies from the test project when no remaining test requires them; no new runtime dependencies are expected.
