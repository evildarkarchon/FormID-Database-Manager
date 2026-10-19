# AGENTS.md

Repository-specific instructions for coding agents. Use `README.md` for the product overview and normal usage.

## Before changing code

- Read `GLOSSARY.md` and the relevant files under `docs/adr/` before changing a domain area. Use the glossary's terms
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
- `FormIdTextIngestion` owns reading, parsing, and counting a FormID text file, and the file's import progress. The
  Store persists only the rows it is handed through the text-row writer role (ADR-0001, ADR-0007).
- `ProcessingRun.cs` and `ProcessingRunControl.cs` contain no user-facing wording. A run reports typed
  `ProcessingRunProgress` and returns a typed `ProcessingRunOutcome`; `ProcessingRunPresentation` renders both, along
  with refusals, failures, and the starting and cancellation-requested facts (ADR-0007, ADR-0012). The retired
  sentences are pinned by name, and `"Would process"` must not reappear anywhere in Core.
- `UserWorkflow.Create` is the only production composition. The WinUI shell supplies the ViewModel, thread
  dispatcher, and file dialog adapter and constructs no Core collaborator. `UserWorkflow` owns the
  `PluginListPresentationAdapter`'s lifetime and detaches it first on dispose.
- `GameContext` owns Game Context state, its transitions, and the Plugin List Source built from it. `UserWorkflow`
  constructs it and forwards Game Context intents to it. `GameContext.cs` contains no user-facing wording and names no
  ViewModel: it reports each state and typed notice to an `IGameContextSink` that `UserWorkflow` implements
  (ADR-0013).
- `ProcessingRunControl` is the single owner of whether a Processing Run is active (ADR-0012). `UserWorkflow` constructs
  it, and it owns the executor. It reads no Game Context, Plugin List, or ViewModel.
- Projected `MainWindowViewModel` state is read-only to consumers, and retired APIs must not be reintroduced.

## Engineering conventions

- Marshal UI changes through `IThreadDispatcher`. Use `SynchronousThreadDispatcher` in tests.
- Keep `GameInstallations.CanonicalizeDataDirectory()` as the only production implementation of the game-root-or-Data
  rule (ADR-0002).
- Resolve Store table names only through the closed `SupportedGameReleases` table (ADR-0003).
- Processing Run Control decides each press of the run button:
  - starts the run Processing Run Admission admits, reports a refusal, or cancels the active run (ADR-0012);
  - takes the `ProcessingRunInputs` that `UserWorkflow` captured;
  - creates the run's cancellation token inside its gate the moment it admits the run, before reporting anything, so a
    cancel pressed during startup is never dropped;
  - reports rendered activity and messages to a sink that `UserWorkflow` implements over the ViewModel, and calls the
    sink while holding its lock to keep the #60 ordering;
  - throws `ObjectDisposedException` for a press after disposal rather than rendering it.
- `ProcessingRunExecutor` executes one typed Processing Run request with the cancellation token Processing Run Control
  hands it, reports its transient progress, and returns how it ended. It holds no run state:
  - it has no cancellation slot, `Cancel()`, or `Dispose`;
  - cancellation of the token it was given is returned as a `CancelledRunOutcome`;
  - `UnresolvableMasterException` and unexpected internal failures propagate as exceptions rather than becoming
    outcomes (ADR-0007).

  Mutagen's `CreateFromBinaryOverlay` is synchronous and cannot be cancelled.
- Processing Run Admission decides, before a run exists, whether a button press starts one (ADR-0010). It is pure: a
  refusal is a typed `RunRejectionReason` that `ProcessingRunPresentation` renders, not an exception, and admission
  creates no directory. The Store creates the database's parent directory when it opens. The selected-Plugin request
  carries its `PluginListSource`, so canonicalization does not repeat downstream.
- A dry run returns a Processing Run Plan and opens no Store. A selected-Plugin dry run still reads the Data directory
  from the request's Plugin List Source, prepares the load order, and opens each Plugin's overlay, so it reports
  would-ingest and would-skip per Plugin rather than echoing the selection. A FormID text dry run reports the file's presence and size, and neither opens nor parses
  it — counting rows is what an import does.
- `ProcessingRunPresentation` is a pure renderer and writes nothing (ADR-0007, ADR-0012).
  - Processing Run Control keeps the run activity and its ordering lock.
  - `UserWorkflow`'s sink adapter performs every ViewModel write.
  - Do not move either into Presentation for symmetry with `PluginListPresentationAdapter`.
  - Test run wording only in `ProcessingRunPresentationTests`. Other tests compare against Presentation's output.
- `GameContextPresentation` is a pure renderer and writes nothing (ADR-0013). It renders each Game Context notice and
  chooses its message list; `UserWorkflow`'s Game Context sink adapter performs every ViewModel write. Test Game
  Context wording only in `GameContextPresentationTests`. `GameContextTests` record what the sink receives.
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

For issue, PRD, or triage work, follow `docs/agents/issue-tracker.md` and `docs/agents/triage-labels.md`. GitHub
Issues are the request surface; external pull requests are not. Existing `.scratch/<feature>/` files are historical
and were not migrated.
