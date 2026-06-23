## 1. Test Inventory

- [x] 1.1 Search the test project for `Avalonia`, `AvaloniaFact`, `Headless`, `UiTestHost`, `TestInitialization`, `WindowManager`, and `AvaloniaThreadDispatcher` references.
- [x] 1.2 Classify each Avalonia-specific test or helper as core-convert, WinUI source/smoke replacement, abstraction-boundary replacement, manual coverage, or retire.
- [x] 1.3 Confirm integration, performance, load, stress, and regression tests are not relying on desktop UI startup unless explicitly marked manual or environment-specific.

## 2. Core-First Test Conversion

- [x] 2.1 Convert `MainWindowViewModelTests` cases that only validate state, messages, filtering, progress, selection, or dispatcher abstraction behavior from `[AvaloniaFact]` to standard xUnit facts.
- [x] 2.2 Use `SynchronousThreadDispatcher` or focused fake dispatchers in converted ViewModel tests so they do not require Avalonia headless startup.
- [x] 2.3 Ensure service, model, and ViewModel tests compile through `FormID Database Manager.Core` and `FormID Database Manager.TestUtilities` without depending on Avalonia UI startup.
- [x] 2.4 Keep or add focused dispatcher tests that prove queued/post behavior where a synchronous dispatcher would hide the behavior under test.

## 3. Replace Avalonia-Specific Coverage

- [x] 3.1 Replace `AvaloniaThreadDispatcherTests` with current dispatcher coverage for core dispatchers and `WinUiThreadDispatcher` behavior.
- [x] 3.2 Replace `WindowManagerTests` with tests around `IFileDialogService` consumers and mocked picker results where the workflow logic can be automated.
- [x] 3.3 Replace Avalonia window, startup, and control tests with WinUI source tests, launch smoke coverage, documented manual coverage, or retire them when equivalent coverage already exists.
- [x] 3.4 Preserve WinUI migration source tests that lock down binding-critical XAML and platform-service wiring.

## 4. Remove Retired Test Infrastructure

- [x] 4.1 Remove unused `Avalonia.Headless.XUnit`, `Avalonia`, and `Avalonia.Themes.Fluent` test package references when no automated test uses them.
- [x] 4.2 Delete `TestInitialization.cs` and `UiTestHost` after replacement coverage is in place.
- [x] 4.3 Remove or update stale `using Avalonia...` directives and comments from converted tests.
- [x] 4.4 Run a final search for `AvaloniaFact`, `Avalonia.Headless`, `UiTestHost`, and `TestInitialization` to confirm no automated headless dependency remains, or document any temporary blocker.

## 5. Windows CI

- [x] 5.1 Add a Windows GitHub Actions workflow for restoring, building, and testing the solution.
- [x] 5.2 Configure the workflow to use CI-safe `dotnet` commands and the repository's existing skip/category conventions for game-installation, manual, performance, load, or stress tests.
- [x] 5.3 Verify the workflow file does not require local-only paths, installed games, desktop picker interaction, or checked-in build outputs.

## 6. Verification and Documentation

- [x] 6.1 Run focused tests for converted ViewModel, dispatcher, platform-service, and WinUI source coverage.
- [x] 6.2 Run `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64`.
- [x] 6.3 Run `dotnet build "FormID Database Manager.slnx"`.
- [x] 6.4 Run `dotnet test "FormID Database Manager.Tests"` or record any environment-specific blocker.
- [x] 6.5 Add durable Phase 7 verification notes covering build, test, CI, remaining-Avalonia-reference, and skipped/manual-test results.
