# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

FormID Database Manager is a **WinUI 3 / Windows App SDK** desktop app that builds SQLite databases of
FormIDs and their EditorID/Name values from Bethesda game plugins (Skyrim, Fallout 4, Starfield, Oblivion),
parsed with Mutagen. .NET 10, C#, nullable enabled.

This is **not** an Avalonia app. Avalonia was migrated away from, and an architecture test fails the build
if an `Avalonia` package reference or `using Avalonia` reappears in the Core project.

## Build, run, test

The solution file is `.slnx` (XML format), not `.sln`. Paths contain spaces — always quote them.

```bash
# Build. CI builds the WinUI project first, separately, with an explicit x64 platform,
# then the solution. The .slnx declares x86 for the WinUI project while the csproj
# defaults to x64, so the explicit -p:Platform=x64 is required, not optional.
dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64
dotnet build "FormID Database Manager.slnx"

# Run
dotnet run --project "FormID Database Manager.WinUI" -p:Platform=x64

# Test (all non-performance tests, matching CI)
dotnet test "FormID Database Manager.Tests" --filter "Category!=ManualPerformance&Category!=PerformanceRegression&Category!=LoadTest&Category!=StressTest"

# Single test class
dotnet test "FormID Database Manager.Tests" --filter "FullyQualifiedName~FormIdRecordStoreTests"

# Coverage, and portable publish
pwsh ./scripts/run-coverage.ps1 [-OpenReport] [-IncludeStressAndLoad]
pwsh ./scripts/publish-portable.ps1
```

**Windows-only by design.** `EnableWindowsTargeting` is deliberately omitted so default solution builds
fail on non-Windows hosts. Do not add it.

There is no `dotnet format` or analyzer gate in CI. The only build-level enforcement is
`<WarningsAsErrors>CS1998</WarningsAsErrors>` (async method lacking `await` is an error) in the Core and
Tests projects.

## Architecture tests are source-level guards

`FormID Database Manager.Tests/Unit/Architecture/` asserts on **source text and reflection**, so refactors
that look harmless can break the build. Among other things these tests require that:

- `Microsoft.Data.Sqlite` appears in exactly one production file, `Core/Services/FormIdRecordStore.cs`
- `ProcessingRun.cs` does *not* mention `IGameLoadOrders`, `IPluginOverlayReader`, or
  `GameInstallations.CanonicalizeDataDirectory`; `PluginIngestion.cs` owns `IGameLoadOrders`, overlay use, and
  canonicalization, while active source guards keep the retired Game Load Order protocols absent
- Specific `MainWindowViewModel` properties have getters and no setters
- Retired member names (`UpdateProgress`, `ResetProgress`, `IsScanning`, `IsProcessing`, `DatabaseService`,
  `DatabaseFixture`, `PluginListManager`, `ProcessingRunEvent`, …) never reappear in any of the four source trees
- `ProcessingRun.cs` contains no user-facing wording at all: a run reports typed `ProcessingRunProgress` and returns
  a typed `ProcessingRunOutcome`, and `ProcessingRunPresentation` renders both. The guard pins the retired sentences
  by name, so reintroducing one there fails the build
- `"Would process"` appears nowhere in the Core project: a dry run reports a substantive plan — would-ingest and
  would-skip per Plugin, and file presence and size for a FormID text file — rather than echoing the selection

Check these tests before renaming anything in Core or reintroducing an old name.

## Key invariants

- **Threading**: all UI updates go through `IThreadDispatcher`. Plugin List presentation membership is
  dispatcher-confined and exposed read-only; the ViewModel uses `Interlocked` for filter reentrancy.
- **SQL injection**: table names come only from the closed `SupportedGameReleases` constant table via
  `ForRelease()`, which throws for an unsupported release. No GameRelease value reaches SQL unchecked
  (ADR-0003).
- **Data-path canonicalization**: `GameInstallations.CanonicalizeDataDirectory()` is the single
  implementation of the game-root-or-Data-directory rule. Production must not add a second helper
  (ADR-0002).
- **Cancellation**: `ProcessingRunExecutor` owns the active run's `CancellationTokenSource`. Mutagen's
  `CreateFromBinaryOverlay` is synchronous and cannot be cancelled.
- **InternalsVisibleTo**: Core exposes internals to the Tests and WinUI projects.

## Testing conventions

- xunit.v3 + Moq. Test naming: `MethodName_StateUnderTest_ExpectedBehavior`.
- Use `SynchronousThreadDispatcher` in tests, never a platform dispatcher.
- Open databases through `FormIdRecordStore.OpenAsync`; use raw SQLite only for workload generation,
  failure injection, or persisted-state inspection.
- **Tests must not depend on a real game installation** — supply the in-memory
  `Tests/Fakes/InMemoryGameInstallationProbe.cs` through `IGameInstallationProbe` (ADR-0002).
- The load, stress, and regression suites are opt-in: set `RUN_MANUAL_PERFORMANCE_TESTS=1` to run them.
  Only the `Performance/` folder carries traits (`ManualPerformance`, `LoadTest`, `StressTest`,
  `PerformanceRegression`).
- Database, Integration, Performance, and UI collections are defined with `DisableParallelization = true`;
  everything else runs in parallel.

## Code style

`.editorconfig` is authoritative. The rules that differ from C#/.NET defaults:

- `csharp_prefer_braces = true:warning` — braces required, severity raised from silent
- `csharp_preserve_single_line_statements = false` — no `if (x) y();` on one line
- `csharp_indent_labels = flush_left`
- Interfaces must be `I`-prefixed PascalCase (warning severity)

## Comments

This project **overrides** the default "no comments" agent behaviour. Comments are welcome and encouraged.

- Prefer WHY-comments over WHAT-comments — explain non-obvious decisions, invariants, and the reasoning
  behind intentional patterns (sync-over-async in disposal, sequential-only cleaning, the single process
  slot).
- Never strip an accurate existing comment as cleanup.
- Add XML doc comments (`///`) on new or substantially rewritten public members unless trivial.

## Line endings

Do not normalize or convert line endings. `core.autocrlf` is configured and handles this correctly in
almost every case — committed content is normalized to LF regardless of what the working copy holds, so
a mixed working tree is not a problem worth fixing.

- Never rewrite a file solely to change its line endings. That is churn, not a fix.
- Do not "fix" a file whose endings differ from its neighbours. Leave it alone.
- Ignore git's `LF will be replaced by CRLF` warning — it is informational, not an error.
- The bar for converting endings is an actual, demonstrated failure (a tool that chokes on CRLF, a test
  asserting on exact bytes). Say what broke before converting anything.

## Reference material

- **Mutagen API**: consult `docs/mutagen/` first — pre-generated per-project API docs with type catalogs and
  method signatures. Fall back to the `Mutagen/` git submodule source only if those are insufficient. The
  submodule is read-only reference material; the app references Mutagen via NuGet, not the submodule.
- **Domain model and decisions**: `CONTEXT.md` is the root of the single-context layout; architectural
  decisions live under `docs/adr/`. See `docs/agents/domain.md`.

## Agent skills

### Issue tracker

For issue and PRD work, use local Markdown under `.scratch/<feature>/`. See `docs/agents/issue-tracker.md`.

### Triage labels

For triage, use the five canonical roles as local ticket status values. See `docs/agents/triage-labels.md`.

### Domain docs

Before domain work, read the single-context `CONTEXT.md` and relevant `docs/adr/` files. See `docs/agents/domain.md`.
