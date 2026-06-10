## Context

The WinUI project is now the supported desktop shell after Avalonia removal. Its project file keeps MSIX tooling enabled and does not currently define publish profiles. README publish guidance is a single generic `dotnet publish` command, and the WinUI processing flow still fills an empty database path from `Directory.GetCurrentDirectory()`, which can resolve to `C:\WINDOWS\system32` for packaged launches.

Phase 9 needs to make release publishing explicit while preserving the packaged MSIX-capable project baseline selected earlier in the migration. The release plan must also support unpackaged output for local distribution scenarios without changing the base project into an unpackaged-only app.

## Goals / Non-Goals

**Goals:**

- Provide clear packaged and unpackaged publish lanes for `FormID Database Manager.WinUI`.
- Keep packaged MSIX capability enabled in the base WinUI project.
- Scope `WindowsPackageType=None` to unpackaged publish profiles or commands only.
- Fix empty database-path generation to use a user-writable default location that works from packaged and unpackaged launches.
- Document the publish commands, runtime expectations, signing/update decisions, and clean-machine verification steps.

**Non-Goals:**

- Submit the app to the Microsoft Store or configure a production signing certificate.
- Redesign the application identity, branding assets, or installer UX beyond what release publishing requires.
- Complete Phase 10 UX, accessibility, and polish work.
- Guarantee single-file publishing unless the active Windows App SDK version and required self-contained properties are verified during implementation.

## Decisions

### Keep the base project packaged-capable

The WinUI project should retain `EnableMsixTooling` and must not set `WindowsPackageType=None` globally. This preserves the packaged release lane and keeps the Visual Studio package/publish workflow available.

Alternative considered: make the base project unpackaged and add a packaged-only override. That would conflict with the migration baseline and make MSIX behavior easier to accidentally regress.

### Use explicit profile names or commands per release lane

Packaged output should have an explicit command or publish profile for MSIX/MSIX bundle creation. Unpackaged output should have separate framework-dependent and self-contained commands or profiles. If single-file output is attempted, it should be a separate opt-in profile with the required self-contained and self-extract settings rather than being implied by the normal unpackaged lane.

Alternative considered: keep one README publish command and document extra properties inline. That is too easy to misuse because packaged and unpackaged WinUI publish settings differ in meaningful ways.

### Default generated database files into app-local user data

When no database path is supplied, WinUI should generate a path under a user-writable app data directory such as `%LOCALAPPDATA%\FormID Database Manager\Databases`, using `GameReleaseHelper.GetSafeTableName(gameRelease)` for the filename. The code should create the directory before processing and reflect the resolved path in the ViewModel.

Alternative considered: keep using the process current directory. That fails the packaged-launch requirement and can put output under system locations depending on activation context.

### Treat packaged distribution as a documented release decision

The implementation should document whether the packaged lane is intended for Store, AppInstaller, or direct MSIX distribution for this phase. If production signing or update infrastructure is not yet available, the documentation should make that status explicit and identify the development/test signing path used for verification.

Alternative considered: leave signing and updates as implicit future work. Phase 9 specifically calls out signing and update flow, so an explicit decision record is needed even if the selected action is a non-production placeholder.

## Risks / Trade-offs

- Windows App SDK publish properties vary by SDK version -> Use documented commands/profiles for the installed SDK and verify them locally before recording the phase checkpoint.
- Single-file WinUI output may not be supported or may require self-extraction behavior -> Keep it optional and document it only if verified with the current Windows App SDK.
- Packaged signing may block clean-machine installation -> Separate development/test signing verification from production certificate decisions and document the selected channel.
- User-writable defaults could surprise users who expected output beside the executable -> Reflect the resolved path in `DatabasePath` and document how to choose a different output path.

## Migration Plan

1. Add packaged and unpackaged publish profiles or equivalent documented commands for the WinUI project.
2. Update empty database-path generation to use an app-local user data directory and add focused automated coverage for the path behavior.
3. Update README and migration-plan phase 9 notes with release commands, runtime expectations, signing/update decisions, and verification results.
4. Build, test, and publish the selected outputs locally.
5. Verify packaged and unpackaged outputs on a clean Windows machine or VM, or record the exact blocker if the environment cannot complete that verification.

Rollback is straightforward because Phase 9 should avoid broad architecture changes: remove the new publish profiles/docs and revert the default database-path helper if release verification reveals a blocking publish issue.

## Open Questions

- Which packaged distribution channel should be treated as the first supported release path: Store, AppInstaller, or direct MSIX?
- Should unpackaged self-contained output become the preferred non-store release artifact, or remain secondary to packaged MSIX?
- Is single-file output supported by the active Windows App SDK version in this repository, and is its first-launch extraction behavior acceptable?
