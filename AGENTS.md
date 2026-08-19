# AGENTS.md

Repository-specific instructions for coding agents. Use `README.md` for the product overview and normal usage.

## Before changing code

- When `graphify-out/graph.json` exists, start codebase questions with `graphify query "<question>"`. Use
  `graphify path` or `graphify explain` for focused relationships, and `graphify-out/wiki/index.md` for broad
  navigation. Run `graphify update .` after code changes. Skip the graph only when investigating stale graph output
  or when the user asks you not to use it.
- Read `CONTEXT.md` and the relevant files under `docs/adr/` before changing a domain area. Use the glossary's terms
  and call out any proposed conflict with an accepted ADR.
- For Mutagen APIs, consult `docs/mutagen/` first. Fall back to the read-only `Mutagen/` submodule only when those
  generated docs are insufficient; production references Mutagen through NuGet.
- `docs/WinUI-Migration-Plan.md` is a historical checkpoint. Do not update it or treat it as current architecture.

## Build and test

The solution uses `.slnx`, paths contain spaces, and the supported build host is Windows. Do not add
`EnableWindowsTargeting`; non-Windows solution builds are intentionally unsupported.

```powershell
# Match the CI build sequence.
dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64
dotnet build "FormID Database Manager.slnx"

# Run the non-performance test suite.
dotnet test "FormID Database Manager.Tests" --filter "Category!=ManualPerformance&Category!=PerformanceRegression&Category!=LoadTest&Category!=StressTest"
```

Use `FullyQualifiedName~TypeOrTestName` to focus a test run. The performance suites are opt-in through
`RUN_MANUAL_PERFORMANCE_TESTS=1`. `CS1998` is a build error in Core and Tests.

## Enforced boundaries

Tests under `FormID Database Manager.Tests/Unit/Architecture/` inspect source text and reflection. Review them before
renaming Core members or changing project boundaries. In particular:

- Core must remain independent of Avalonia and other desktop UI implementations.
- `FormIdRecordStore.cs` is the only production source file that may reference `Microsoft.Data.Sqlite`.
- `ProcessingRun` owns Store opening, optimization, and disposal; `PluginIngestion` owns load-order, overlay, and
  Data-path adapters.
- `ProcessingRun.cs` contains no user-facing wording. A run reports typed `ProcessingRunProgress` and returns a typed
  `ProcessingRunOutcome`; `ProcessingRunPresentation` renders both (ADR-0007). The retired sentences are pinned by
  name, and `"Would process"` must not reappear anywhere in Core.
- Projected `MainWindowViewModel` state is read-only to consumers, and retired APIs must not be reintroduced.

## Engineering conventions

- Marshal UI changes through `IThreadDispatcher`. Use `SynchronousThreadDispatcher` in tests.
- Keep `GameInstallations.CanonicalizeDataDirectory()` as the only production implementation of the game-root-or-Data
  rule (ADR-0002).
- Resolve Store table names only through the closed `SupportedGameReleases` table (ADR-0003).
- `ProcessingRunExecutor` executes one typed Processing Run request, reports its transient progress, and returns how it
  ended. It owns active-run cancellation: because the token is executor-owned, cancellation is returned as a
  `CancelledRunOutcome`, while validation failures, `UnresolvableMasterException`, and unexpected internal failures
  propagate as exceptions rather than becoming outcomes (ADR-0007). Mutagen's `CreateFromBinaryOverlay` is synchronous
  and cannot be cancelled.
- A dry run returns a Processing Run Plan and opens no Store. A selected-Plugin dry run still resolves the Data path,
  prepares the load order, and opens each Plugin's overlay, so it reports would-ingest and would-skip per Plugin rather
  than echoing the selection. A FormID text dry run reports the file's presence and size, and neither opens nor parses
  it — counting rows is what an import does.
- `ProcessingRunPresentation` is a pure renderer and writes nothing. `UserWorkflow` keeps the run activity, its
  ordering lock, and every ViewModel write, so do not move them there for symmetry with
  `PluginListPresentationAdapter` (ADR-0007).
- Open databases through `FormIdRecordStore.OpenAsync`. Use raw SQLite only for workload generation, failure
  injection, or persisted-state inspection.
- Tests must not depend on an installed game. Supply an in-memory `IGameInstallationProbe`.
- Name tests `MethodName_StateUnderTest_ExpectedBehavior`.
- Database, Integration, Performance, and UI test collections run without parallelization; other tests may run in
  parallel.
- Follow `.editorconfig`. Do not normalize line endings or react to Git's LF/CRLF warning unless an actual tool or test
  fails because of exact line endings.

## Comments and documentation

- Preserve accurate comments. Remove or rewrite one only when its code is deleted or the comment became misleading,
  and mention that change in the handoff.
- Add concise WHY-comments for hidden constraints, race or cancellation handling, lifetime ownership, ordering, and
  workarounds. Explain intentionally empty `catch` or `finally` blocks.
- Add idiomatic doc comments to new or substantially rewritten methods. Trivial private helpers are exempt.

## Repository workflows

For issue, PRD, or triage work, follow `docs/agents/issue-tracker.md` and `docs/agents/triage-labels.md`. GitHub Issues
in `evildarkarchon/FormID-Database-Manager` are the request surface; external pull requests are not.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
