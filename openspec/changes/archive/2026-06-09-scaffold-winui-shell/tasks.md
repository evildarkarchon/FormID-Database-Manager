## 1. Scaffold Baseline

- [x] 1.1 Reconfirm `dotnet new list winui` exposes the C# WinUI template before scaffolding.
- [x] 1.2 Dry-run the packaged scaffold command for `FormID Database Manager.WinUI` with `net10.0`, `--unpackaged false`, and `--no-solution-file`.
- [x] 1.3 Scaffold `FormID Database Manager.WinUI` from the WinUI template without overwriting existing files.
- [x] 1.4 Inspect the generated project to confirm packaged MSIX files and Windows App SDK configuration are present.

## 2. Solution Integration

- [x] 2.1 Add the WinUI shell project to `FormID Database Manager.slnx`.
- [x] 2.2 Add a `ProjectReference` from the WinUI shell to `FormID Database Manager.Core`.
- [x] 2.3 Keep generated WinUI startup files minimal and avoid porting Avalonia UI, dispatcher, picker, or processing workflows.
- [x] 2.4 Confirm no extra generated solution files or unintended template artifacts remain in the repo root.

## 3. Verification

- [x] 3.1 Build the WinUI shell project directly.
- [x] 3.2 Build `FormID Database Manager.slnx` with both Avalonia and WinUI projects included.
- [x] 3.3 Verify the existing Avalonia project still builds and remains in the solution.
- [x] 3.4 Launch the blank WinUI app through the packaged local workflow and record objective window evidence or the blocking environment/deployment issue.
- [x] 3.5 Document Phase 2 verification notes in the existing WinUI migration checkpoint documentation.

## 4. Final Review

- [x] 4.1 Search the WinUI shell for unintended Avalonia references.
- [x] 4.2 Review generated package identity/signing defaults and note any Phase 9 deployment follow-up.
- [x] 4.3 Update this task list with completed items and summarize remaining risks.

## Remaining Risks

- Package identity, publisher, display name, signing certificate, and distribution channel are still template defaults and remain a Phase 9 deployment decision.
- The generated `MsixPackage` launch profile is not supported by `dotnet run`; local packaged verification currently uses loose MSIX registration plus `shell:AppsFolder`, while Visual Studio deploy/F5 remains the expected IDE workflow.
- The WinUI shell is intentionally blank and template-first; Avalonia UI, WinUI picker/dispatcher services, and production processing workflows are deferred to later phases.
