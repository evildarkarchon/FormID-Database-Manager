## 1. Publish Lane Configuration

- [x] 1.1 Inspect the current Windows App SDK publish properties and confirm which packaged and unpackaged commands/profiles are supported by the installed SDK.
- [x] 1.2 Add or document a packaged MSIX/MSIX bundle publish lane for `FormID Database Manager.WinUI` without setting `WindowsPackageType=None` globally.
- [x] 1.3 Add or document an unpackaged framework-dependent publish lane with `WindowsPackageType=None` scoped to that lane and runtime prerequisites stated.
- [x] 1.4 Add or document an unpackaged self-contained publish lane with `WindowsPackageType=None` scoped to that lane and Windows App SDK runtime behavior stated.
- [x] 1.5 Decide whether single-file publishing is supported for this phase and either add a verified opt-in lane with required properties or document why it is not selected.

## 2. Writable Default Database Path

- [x] 2.1 Replace the WinUI empty database-path fallback that uses `Directory.GetCurrentDirectory()` with a user-writable app data location.
- [x] 2.2 Ensure the generated database directory is created before processing starts and the resolved path is assigned back to `MainWindowViewModel.DatabasePath`.
- [x] 2.3 Add focused automated coverage for generated default database paths, including safe table-name filenames and avoidance of current-directory/system-directory defaults.

## 3. Release Documentation

- [x] 3.1 Update README build, run, and publish guidance with the supported WinUI packaged and unpackaged release commands or publish profile names.
- [x] 3.2 Document packaged distribution channel, signing status, and update-flow decision for this phase.
- [x] 3.3 Document unpackaged runtime prerequisites for framework-dependent and self-contained outputs.
- [x] 3.4 Update `docs/WinUI-Migration-Plan.md` with Phase 9 implementation and verification notes.

## 4. Verification

- [x] 4.1 Run `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64`.
- [x] 4.2 Run `dotnet build "FormID Database Manager.slnx"`.
- [x] 4.3 Run `dotnet test "FormID Database Manager.Tests"`.
- [x] 4.4 Publish the selected packaged output and record the artifact path or blocker.
- [x] 4.5 Publish the selected unpackaged output and record the artifact path or blocker.
- [x] 4.6 Verify packaged and unpackaged outputs on a clean Windows machine or VM, or record the exact environment blocker.
