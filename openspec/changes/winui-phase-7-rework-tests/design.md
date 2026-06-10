## Context

The WinUI migration has reached the point where the shared core owns most reusable application behavior and the WinUI shell has parity for the main workflow. The test project still carries Avalonia-specific infrastructure: `Avalonia.Headless.XUnit`, `TestInitialization.cs`, `UiTestHost`, `[AvaloniaFact]` usage in ViewModel tests, Avalonia UI tests, and tests for Avalonia platform services such as `WindowManager` and `AvaloniaThreadDispatcher`.

Phase 7 is the transition from "Avalonia can still host tests" to "core and WinUI coverage are sufficient for later Avalonia removal." It should not remove the Avalonia application itself; that remains Phase 8. It should reduce or eliminate test dependencies on Avalonia once replacement coverage exists.

## Goals / Non-Goals

**Goals:**

- Move UI-neutral service and ViewModel behavior tests to regular xUnit execution through the core project and test utilities.
- Convert ViewModel tests that only need state changes, message collection notifications, filtering, or dispatcher abstraction behavior from `[AvaloniaFact]` to `[Fact]`.
- Replace Avalonia dispatcher, window startup, picker, and UI tests with WinUI source tests, mockable platform-service abstraction tests, or documented smoke/manual coverage where real desktop UI is required.
- Remove Avalonia headless test packages and initialization helpers after there are no remaining automated tests that need them.
- Add Windows CI coverage for build and automated tests.
- Record Phase 7 verification in durable change verification notes.

**Non-Goals:**

- Removing Avalonia from the application project or deleting Avalonia production files; that is Phase 8.
- Rewriting the WinUI layout, processing workflow, packaging model, or deployment flow.
- Adding a broad desktop UI automation framework unless a narrow smoke check is practical and stable.
- Changing Mutagen, SQLite, or game-installation integration behavior outside what is needed to keep existing tests compiling and focused.

## Decisions

1. Prefer core-first unit tests over UI-hosted tests for shared behavior.

   `MainWindowViewModel`, `PluginListItem`, processing services, database services, and game detection behavior are now available through `FormID Database Manager.Core`. Tests for those types should reference core behavior directly and use `SynchronousThreadDispatcher` or purpose-built fake dispatchers. The alternative is keeping `[AvaloniaFact]` around as a generic async/UI harness, but that keeps the suite coupled to the framework that Phase 8 intends to remove.

2. Treat WinUI coverage as source-level and smoke-level unless behavior truly needs live desktop automation.

   Existing WinUI source tests already lock down important wiring in XAML and code-behind. Phase 7 should extend that style for binding-critical and platform-service construction checks, and use a launch-time smoke/manual verification only for behavior that cannot be tested without a real WinUI window or picker. The alternative is replacing Avalonia headless tests with a large UI automation layer, which is likely brittle and unnecessary for this migration step.

3. Replace platform-service tests at the abstraction boundary.

   `WindowManagerTests` should become tests around `IFileDialogService` consumers and mocked picker results where possible. Avalonia dispatcher tests should be replaced by existing core dispatcher tests and WinUI dispatcher coverage that proves queued invocation, access checks, and exception propagation. The alternative is preserving tests for production services that will be removed, which makes the suite report confidence in the wrong platform.

4. Keep integration and performance tests centered on data behavior.

   Mutagen, SQLite, load-order, game-installation, performance, load, stress, and regression tests should continue to validate processing behavior. Phase 7 should avoid converting those tests into UI tests or coupling them to WinUI. The alternative would make CI and local verification more fragile without improving confidence in the migration.

5. Add Windows CI after local test dependencies are stable.

   The CI workflow should build the solution and run the automated tests on a Windows runner, using existing `dotnet` commands and test filters/categories only where the repository already marks tests as environment-specific or manual. The alternative is delaying CI until after Avalonia removal, but Phase 7 is the right point to catch Windows-only WinUI project and test regressions before Phase 8.

## Risks / Trade-offs

- Removing Avalonia headless before replacement coverage exists -> Mitigation: first map each Avalonia UI test to core, WinUI source, smoke, manual, or retired coverage before deleting infrastructure.
- ViewModel tests may hide dispatcher bugs if every test uses a synchronous dispatcher -> Mitigation: keep focused fake-dispatcher tests for queued/post behavior, especially filtering and message/progress updates.
- WinUI picker behavior is hard to automate reliably -> Mitigation: test `IFileDialogService` consumers with mocks and document real picker behavior as smoke/manual verification.
- Windows CI may hit environment-specific integration tests that require game installs or desktop interaction -> Mitigation: rely on existing custom attributes and categories, and keep CI to automated tests that are designed to run on clean runners.

## Migration Plan

1. Inventory all remaining Avalonia-specific test references and classify them as core-convert, WinUI source/smoke, abstraction-boundary, manual, or retire.
2. Convert ViewModel and service tests to standard xUnit facts where they do not require a UI runtime.
3. Replace Avalonia dispatcher, window, startup, and picker tests with core dispatcher tests, WinUI dispatcher/source tests, `IFileDialogService` consumer tests, or documented smoke/manual checks.
4. Remove unused Avalonia headless infrastructure and package references from the test project once the suite no longer references them.
5. Add a Windows CI workflow for restore/build/test using the solution and existing test project.
6. Run focused tests, build the WinUI project, build the solution, run the full automated test suite, and record Phase 7 verification in durable change verification notes.

## Open Questions

- Should any current Avalonia UI tests be preserved temporarily until Phase 8, or should Phase 7 retire them once equivalent core/WinUI source coverage exists?
- Should the Windows CI workflow run every test by default, or use a filter to exclude known manual/performance categories on pull requests?
