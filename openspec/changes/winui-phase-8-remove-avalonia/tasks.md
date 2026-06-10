## 1. Inventory

- [x] 1.1 Search active solution, project, source, test, and current documentation files for `Avalonia`, `axaml`, `AvaloniaFact`, and `Headless` references, excluding archived OpenSpec material and ignored build outputs.
- [x] 1.2 Classify each remaining active match as remove, replace with WinUI/current guidance, or intentionally historical documentation.
- [x] 1.3 Confirm any behavior still present only in the legacy Avalonia project has already been ported to `FormID Database Manager.Core` or `FormID Database Manager.WinUI` before deleting the source.

## 2. Remove Legacy Avalonia App

- [x] 2.1 Remove `FormID Database Manager/FormID Database Manager.csproj` from `FormID Database Manager.slnx`.
- [x] 2.2 Delete the legacy Avalonia app startup, AXAML views, converter, dispatcher, picker service, manifest, and project file from the active source tree, or move them to an agreed archive location outside the buildable solution.
- [x] 2.3 Remove any stale references from active project files, source files, or tests that pointed at the legacy Avalonia app project.
- [x] 2.4 Confirm `FormID Database Manager.Core`, `FormID Database Manager.WinUI`, `FormID Database Manager.Tests`, and `FormID Database Manager.TestUtilities` still restore through their intended project references.

## 3. Update Current Guidance

- [x] 3.1 Update current developer guidance files so the project overview describes the WinUI desktop app and core/test project layout accurately.
- [x] 3.2 Update build, run, test, and publish commands that still point to the removed Avalonia project so they use the WinUI project or solution as appropriate.
- [x] 3.3 Update `docs/WinUI-Migration-Plan.md` with the Phase 8 completion checkpoint after verification is finished.
- [x] 3.4 Leave archived OpenSpec changes and explicitly historical migration notes intact unless they create active developer confusion.

## 4. Verification

- [x] 4.1 Run focused reference searches for `Avalonia`, `axaml`, `AvaloniaFact`, and `Headless` and record whether remaining matches are historical only.
- [x] 4.2 Run `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64`.
- [x] 4.3 Run `dotnet build "FormID Database Manager.slnx"`.
- [x] 4.4 Run `dotnet test "FormID Database Manager.Tests"` or record any environment-specific blocker.
- [x] 4.5 Record Phase 8 verification notes covering removal scope, search results, build results, test results, and remaining historical references.
