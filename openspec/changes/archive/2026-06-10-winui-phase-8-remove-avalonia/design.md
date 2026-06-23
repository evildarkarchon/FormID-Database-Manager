## Context

The migration plan has completed the WinUI shell, platform services, workflow parity, binding hardening, and Phase 7 test rework. The solution now contains `FormID Database Manager.Core`, `FormID Database Manager.WinUI`, `FormID Database Manager.Tests`, and `FormID Database Manager.TestUtilities`, but it still also includes the legacy `FormID Database Manager` Avalonia project with AXAML views, Avalonia startup, Avalonia platform services, and Avalonia package references.

Phase 8 is the handoff from a dual-shell migration state to a WinUI-only application state. The shared core remains the stable application behavior boundary; the WinUI project becomes the only supported desktop shell. Historical migration notes and archived OpenSpec changes can continue to mention Avalonia as history, but buildable source, active tests, and current developer guidance should not require Avalonia.

## Goals / Non-Goals

**Goals:**

- Remove the legacy Avalonia application project from the solution and active source tree.
- Remove Avalonia package references, AXAML files, Avalonia converters, Avalonia dispatchers, Avalonia picker services, and any remaining active test references.
- Keep the core project UI-neutral and keep the WinUI project as the only desktop app startup path.
- Update current developer guidance and migration documentation so build/run commands point to WinUI.
- Verify removal with targeted reference searches, WinUI build, solution build, and the automated test suite.

**Non-Goals:**

- Reworking WinUI layout, UX polish, packaging/publish strategy, signing, or deployment distribution; those remain later phases.
- Changing Mutagen parsing, SQLite schema, processing behavior, game detection, plugin list behavior, or ViewModel contracts except where references need cleanup.
- Editing archived OpenSpec changes or historical migration records solely to remove past mentions of Avalonia.
- Introducing a new desktop UI automation framework.

## Decisions

1. Remove the legacy Avalonia project from the solution instead of keeping it as an unused compatibility shell.

   Phase 8's purpose is to eliminate the maintenance cost and ambiguity of two application shells. Keeping a non-primary Avalonia project buildable would preserve the dependencies this phase is meant to remove and would make build failures harder to classify. The alternative is leaving the project in place but out of docs, which still allows stale code and packages to drift.

2. Treat `FormID Database Manager.Core` as the only behavior preservation boundary.

   The cleanup should not move business logic into WinUI or duplicate old Avalonia code. Core already owns ViewModels, models, processing, database, game detection, load-order services, file-dialog abstraction, and dispatcher abstraction. The alternative is copying any remaining useful code out of the Avalonia project during deletion, but Phase 7 should already have moved behavior that needs to survive.

3. Keep WinUI startup and platform services narrow.

   `FormID Database Manager.WinUI` should remain the only desktop entry point and should continue owning WinUI-specific dispatcher, file dialog, XAML, and window lifecycle code. The Phase 8 implementation may adjust docs, solution membership, or project references, but should avoid broad WinUI behavior changes unless required to keep the app building.

4. Distinguish active references from historical references.

   Searches for `Avalonia`, `axaml`, `AvaloniaFact`, and `Headless` should fail for active source, tests, and project files after cleanup. Mentions in archived OpenSpec changes, migration history, or documentation explaining completed phases may remain if they are clearly historical. The alternative is rewriting history-heavy documentation, which adds churn without improving the buildable application surface.

5. Use verification before and after destructive cleanup.

   Phase 8 should inventory remaining active references before deletion, then rerun the same searches after deletion. Build and test verification should include the WinUI project build with an explicit platform, full solution build, and full automated test suite. The alternative is relying on file deletion alone, which can miss stale docs, project references, or generated assumptions.

## Risks / Trade-offs

- Removing a source file before its behavior was fully ported -> Mitigation: inventory remaining Avalonia files first and confirm surviving behavior is represented in core or WinUI before deletion.
- Solution/project cleanup can leave stale references that only fail in CI or Release builds -> Mitigation: run explicit WinUI build, full solution build, and full tests after removal.
- Broad reference searches may report historical migration docs -> Mitigation: separate active source/test/project references from allowed archived or historical mentions in the verification notes.
- Existing generated `bin`/`obj` outputs may contain stale Avalonia text or assemblies -> Mitigation: do not use build outputs as the source of truth; clean ignored outputs if they interfere with verification or scope searches to tracked/active files.
- Removing the old app changes developer muscle memory and commands -> Mitigation: update current guidance files and the migration plan with WinUI run/build commands.

## Migration Plan

1. Inventory active Avalonia references in solution, project, source, test, and current documentation files.
2. Remove `FormID Database Manager/FormID Database Manager.csproj` from `FormID Database Manager.slnx` and delete the legacy Avalonia application source that no active project should compile.
3. Clean current docs and agent guidance so the project overview, build/run commands, and tech stack describe the WinUI application and no longer list Avalonia as an active dependency.
4. Confirm core, test utilities, tests, and WinUI project references remain correct after the old project is gone.
5. Run focused reference searches for active source/test/project files, then build the WinUI project, build the solution, run the automated tests, and record Phase 8 verification in `docs/WinUI-Migration-Plan.md`.

## Open Questions

- Should the legacy Avalonia project directory be deleted outright during Phase 8, or moved under an archive location outside the buildable solution?
- Should historical guidance files such as `AGENTS.md` and `CLAUDE.md` retain any mention of Avalonia in migration history, or should they describe only the current WinUI state after this phase?
