## ADDED Requirements

### Requirement: Phase 7 tests validate UI-neutral behavior through the core path
The system SHALL run service, model, and ViewModel tests for UI-neutral behavior through the `FormID Database Manager.Core` project and standard xUnit execution unless a test explicitly requires a platform UI runtime.

#### Scenario: ViewModel state tests run without Avalonia
- **WHEN** Phase 7 ViewModel tests validate property changes, message visibility, plugin filtering, selection state, progress state, or dispatcher abstraction behavior
- **THEN** they use regular xUnit facts with UI-neutral test dispatchers instead of `AvaloniaFact`

#### Scenario: Service tests reference core behavior
- **WHEN** Phase 7 service tests validate database, processing, game detection, plugin loading, text processing, or load-order behavior
- **THEN** they compile against the shared core project and test utilities without requiring Avalonia UI startup

### Requirement: Avalonia headless test infrastructure is retired after replacement coverage exists
The system SHALL remove Avalonia headless test infrastructure from the automated test project after remaining Avalonia-specific tests have been converted, replaced, explicitly marked manual, or retired.

#### Scenario: Headless package references are removed
- **WHEN** Phase 7 test rework is complete
- **THEN** the test project no longer references `Avalonia.Headless.XUnit` or uses `AvaloniaFact` for automated behavior tests

#### Scenario: Test host files are removed
- **WHEN** Phase 7 test rework is complete
- **THEN** `TestInitialization.cs` and `UiTestHost` are removed from the automated test project unless a documented temporary blocker keeps them for Phase 8

### Requirement: WinUI and platform-service tests replace Avalonia UI service tests
The system SHALL replace tests for Avalonia-specific window, dispatcher, startup, and picker behavior with WinUI-aligned source, smoke, or abstraction-boundary tests.

#### Scenario: Dispatcher coverage targets current dispatchers
- **WHEN** Phase 7 verifies dispatcher behavior
- **THEN** tests cover core dispatcher behavior and WinUI dispatcher access, queueing, awaited invocation, and exception propagation instead of `AvaloniaThreadDispatcher`

#### Scenario: Picker coverage targets file-dialog abstraction
- **WHEN** Phase 7 verifies file and folder picker workflow logic
- **THEN** tests use `IFileDialogService` consumers and mockable picker results for automated coverage, while real picker behavior is documented as smoke or manual verification when needed

#### Scenario: WinUI surface coverage remains migration-focused
- **WHEN** Phase 7 verifies WinUI window wiring
- **THEN** source or smoke tests cover binding-critical and platform-service construction behavior without depending on Avalonia controls or AXAML files

### Requirement: Integration and performance tests remain data-behavior focused
The system SHALL keep integration, performance, load, stress, and regression tests focused on Mutagen, SQLite, processing, and game-environment behavior rather than UI framework mechanics.

#### Scenario: Data tests do not require desktop UI infrastructure
- **WHEN** Phase 7 runs integration and performance-oriented tests
- **THEN** those tests do not require Avalonia headless startup, WinUI window activation, or file picker dialogs unless explicitly marked as manual or environment-specific

### Requirement: Windows CI runs automated migration tests
The system SHALL provide a Windows CI workflow that restores, builds, and runs the automated test suite for the WinUI migration branch.

#### Scenario: CI builds and tests on Windows
- **WHEN** the Windows CI workflow runs on a clean runner
- **THEN** it builds the solution and runs the automated test project using the repository's documented `dotnet` commands or equivalent CI-safe variants

#### Scenario: CI respects environment-specific tests
- **WHEN** tests require installed games, manual desktop interaction, or performance/load/stress execution
- **THEN** the CI workflow excludes, skips, or reports them using the repository's existing test attributes and category conventions

### Requirement: Phase 7 test rework is verified
The system SHALL verify and document Phase 7 test rework before Phase 8 Avalonia removal begins.

#### Scenario: Automated verification is recorded
- **WHEN** Phase 7 verification is complete
- **THEN** durable change verification notes record focused test results, WinUI project build results, full solution build results, full automated test results, and any skipped/manual test rationale

#### Scenario: Remaining Avalonia test references are known
- **WHEN** Phase 7 verification is complete
- **THEN** the migration notes identify that automated test infrastructure is free of Avalonia headless dependencies or records the specific remaining blocker that must be resolved before Phase 8
