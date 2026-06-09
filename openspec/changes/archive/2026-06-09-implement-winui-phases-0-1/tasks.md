## 1. Phase 0 Baseline

- [x] 1.1 Confirm the migration branch/checkpoint and current worktree state
- [x] 1.2 Run `dotnet build "FormID Database Manager.slnx"` and record the baseline result
- [x] 1.3 Run `dotnet test "FormID Database Manager.Tests"` and record the baseline result
- [x] 1.4 Verify `dotnet new list winui` exposes the WinUI template
- [x] 1.5 Document packaged MSIX as the selected target deployment model for the staged migration

## 2. Test-First Core Boundary

- [x] 2.1 Add a failing test or build check showing the new core project is required in the solution
- [x] 2.2 Add a failing test or build check showing the core project must not reference Avalonia
- [x] 2.3 Add a failing test or build check showing the core file-dialog abstraction is required

## 3. Phase 1 Core Extraction

- [x] 3.1 Add `FormID Database Manager.Core` as a `.NET 10` class library
- [x] 3.2 Move UI-neutral models into the core project
- [x] 3.3 Move UI-neutral ViewModels into the core project
- [x] 3.4 Move UI-neutral services and service interfaces into the core project
- [x] 3.5 Keep Avalonia-specific dispatcher and picker implementations in the current app project
- [x] 3.6 Add `IFileDialogService` to the core project
- [x] 3.7 Update app, tests, and test utilities project references

## 4. Verification

- [x] 4.1 Build the full solution after extraction
- [x] 4.2 Run the full test suite after extraction
- [x] 4.3 Search for unintended Avalonia references in the core project
- [x] 4.4 Update task checkboxes and summarize remaining risks
