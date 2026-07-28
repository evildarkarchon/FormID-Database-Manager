# Graph Report - .  (2026-07-28)

## Corpus Check
- 116 files · ~167,529 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1677 nodes · 4007 edges · 72 communities (66 shown, 6 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 274 edges (avg confidence: 0.81)
- Token cost: unavailable (semantic-agent usage was not exposed by this Codex host)

## Community Hubs (Navigation)
- Plugin List Refresh
- Plugin Ingestion Tests
- Processing Run Execution
- Game Detection
- User Workflow Tests
- Record Store Opening
- Thread Dispatching
- Architecture Domain Concepts
- Testing and SQLite Docs
- Main ViewModel Tests
- Record Store Core
- Store Domain Records
- Processing Executor Tests
- Ingestion Store Contracts
- Load and Stress Tests
- Solution Project Structure
- Main Window XAML
- Platform Boundary Tests
- Database Benchmarks
- Database Integration Tests
- Service Test Namespaces
- Performance Regression Tests
- Plugin Ingestion Core
- ViewModel Collections
- Test Environment Attributes
- Discovery Result Model
- User Workflow Service
- Ingestion Contract Tests
- Graphify Tooling
- Main Window Events
- Memory Benchmarks
- Controlled Discovery Fakes
- Issue Triage Governance
- Store Opener Concurrency
- Presentation Adapter Outcomes
- Discovery Behavioral Tests
- Performance Test Projects
- Architecture Boundary Tests
- ViewModel Concurrency Tests
- ViewModel UI Bindings
- Plugin List Presentation
- Ingestion Outcome Contracts
- Discovery Test Doubles
- Game Load Order Provider
- Plugin List Source
- Entry Extraction
- File Dialog Selection
- ViewModel Processing Commands
- Installation Attribute Tests
- xUnit Runner Configuration
- Load Order Abstraction
- Plugin List Discovery
- Game Context Projection
- Default Database Path
- Store Session Test Doubles
- Test Mock Factory
- Recording Thread Dispatcher
- Load Order Snapshot
- Packaging and Distribution
- WinUI Application Startup
- Processing Warning Collection
- Test Collections
- Sequenced Discovery
- Game Location Service
- Game Location Contract
- Synchronous Test Progress
- Plugin Name Validation

## God Nodes (most connected - your core abstractions)
1. `MainWindowViewModelTests` - 83 edges
2. `UserWorkflowTests` - 63 edges
3. `FormID_Database_Manager.Services` - 59 edges
4. `PluginIngestionTests` - 47 edges
5. `MainWindowViewModel` - 44 edges
6. `ProcessingRunExecutorTests` - 44 edges
7. `UserWorkflow` - 42 edges
8. `FormIdRecordStoreTests` - 40 edges
9. `Window` - 40 edges
10. `FormIdRecordStore` - 35 edges

## Surprising Connections (you probably didn't know these)
- `Make Game Context Authoritative` --semantically_similar_to--> `Make Game Context Value-Owning`  [INFERRED] [semantically similar]
  Architecture review 20260722 1.html → Architecture review 20260721 1.pdf
- `Hide Mutagen Inside Plugin Reading` --semantically_similar_to--> `Deepen the Mutagen-Facing Plugin Read Seam`  [INFERRED] [semantically similar]
  Architecture review 20260722 1.html → Architecture review 20260721 2.pdf
- `Give Processing Run One Reporting Implementation` --semantically_similar_to--> `Make Processing Run Outcomes Authoritative`  [INFERRED] [semantically similar]
  Architecture review 20260722 1.html → Architecture review 20260721 1.pdf
- `Internalize Plugin List Projection Lifetime` --semantically_similar_to--> `Give Plugin List Projection One Owner`  [INFERRED] [semantically similar]
  Architecture review 20260722 1.html → Architecture review 20260721 1.pdf
- `Contract Test-Only Store Readback Surface` --conceptually_related_to--> `FormID Record Store`  [INFERRED]
  Architecture review 20260721 2.pdf → CONTEXT.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Graphify Extraction and Analysis Pipeline** — _codex_skills_graphify_skill_graphify, _codex_skills_graphify_skill_structural_and_semantic_extraction, _codex_skills_graphify_skill_provenance_confidence, _codex_skills_graphify_skill_community_outputs [EXTRACTED 1.00]
- **FormID Processing Domain Flow** — context_user_workflow, context_game_context, context_plugin_list, context_processing_run, context_formid_record_store [EXTRACTED 1.00]
- **Deep Module Architecture Candidates** — architecture_review_20260722_1_make_game_context_authoritative, architecture_review_20260722_1_hide_mutagen_inside_plugin_reading, architecture_review_20260722_1_unify_plugin_scoped_store_commits, architecture_review_20260722_1_put_advanced_mode_behind_plugin_list, architecture_review_20260722_1_processing_run_reporting, architecture_review_20260722_1_internalize_plugin_list_projection_lifetime [EXTRACTED 1.00]
- **Programmatic Test Data Ecosystem** — formid_database_manager_tests_testdata_readme_test_data_builder, formid_database_manager_tests_testdata_readme_plugin_test_data, formid_database_manager_tests_testdata_readme_formid_list_test_data, formid_database_manager_tests_testdata_readme_plugin_list_test_data, docs_testing_best_practices_test_data_management [INFERRED 0.95]
- **Store-Centered SQLite Test Boundary** — docs_adr_0001_formid_record_store_owns_sqlite_sqlite_ownership, docs_adr_0001_formid_record_store_owns_sqlite_formidrecordstore_openasync, docs_testing_best_practices_formid_record_store_testing_pattern, docs_testing_best_practices_raw_sqlite_inspection_boundary, docs_testing_best_practices_database_tests_collection [INFERRED 0.95]
- **Agent Governance Conventions** — docs_agents_domain_single_context_layout, docs_agents_issue_tracker_issues_only_triage_surface, docs_agents_triage_labels_canonical_triage_roles [INFERRED 0.85]

## Communities (72 total, 6 thin omitted)

### Community 0 - "Plugin List Refresh"
Cohesion: 0.05
Nodes (41): EventArgs, EventHandler, CancellationToken, CancellationTokenSource, GameRelease, ImmutableArray, int, IReadOnlySet (+33 more)

### Community 1 - "Plugin Ingestion Tests"
Cohesion: 0.06
Nodes (34): Exception, GameRelease, GameReleaseHelper, BinaryReadParameters, GameRelease, IModDisposeGetter, IPluginOverlayReader, MutagenPluginOverlayReader (+26 more)

### Community 2 - "Processing Run Execution"
Cohesion: 0.06
Nodes (34): Consumer, bool, CancellationToken, CancellationTokenSource, GameRelease, int, IProgress, IReadOnlyList (+26 more)

### Community 3 - "Game Detection"
Cohesion: 0.06
Nodes (22): detectionData, GameRelease, HashSet, GameDetectionService, Fact, List, string, Task (+14 more)

### Community 4 - "User Workflow Tests"
Cohesion: 0.10
Nodes (13): AppWindow, FileDialogResult, FileDialogResultKind, Fact, GameRelease, Mock, string, Task (+5 more)

### Community 5 - "Record Store Opening"
Cohesion: 0.09
Nodes (23): GameRelease, Fact, GameRelease, IReadOnlyList, string, Task, FixedGameLoadOrderProvider, ProcessingRunIntegrationTests (+15 more)

### Community 6 - "Thread Dispatching"
Cohesion: 0.06
Nodes (27): DispatcherQueue, Action, Task, ImmediateThreadDispatcher, IThreadDispatcher, Action, Func, string (+19 more)

### Community 7 - "Architecture Domain Concepts"
Cohesion: 0.06
Nodes (52): FormID Record Store, Plugin Ingestion, Plugin List Ownership, Processing Run Executor, FormID Database Manager Project Architecture, User Workflow, Architecture Review 21 July 2026 at 16:00 PDT, Deepen Plugin Ingestion Across Selected Plugins (+44 more)

### Community 8 - "Testing and SQLite Docs"
Cohesion: 0.05
Nodes (51): ADR-0001: FormID Record Store Owns Production SQLite Behavior, SQLite Architecture Verification Boundary, Cleanup-Only DisposeAsync, Explicit Successful-Run Optimization, FormIdRecordStore.OpenAsync, Processing Run Substitution Seam, Raw SQLite Client Exception, Selected GameRelease Schema Transaction (+43 more)

### Community 10 - "Record Store Core"
Cohesion: 0.12
Nodes (17): CancellationToken, IEnumerable, int, IProgress, IReadOnlyList, IReadOnlySet, List, SqliteConnection (+9 more)

### Community 11 - "Store Domain Records"
Cohesion: 0.12
Nodes (17): FormIdPluginWriteResult, FormIdRecord, FormIdStoreProgress, FormIdTextFileImportResult, UpdateMode, IEnumerable, IProgress, CancellationToken (+9 more)

### Community 12 - "Processing Executor Tests"
Cohesion: 0.20
Nodes (3): Fact, Task, ProcessingRunExecutorTests

### Community 13 - "Ingestion Store Contracts"
Cohesion: 0.12
Nodes (25): DatabasePath, IFormIdRecordStoreSession, CancellationToken, GameRelease, ImmutableArray, IProgress, RequiresUnreferencedCode, Task (+17 more)

### Community 14 - "Load and Stress Tests"
Cohesion: 0.11
Nodes (18): ITestOutputHelper, List, ManualPerformanceFact, string, Task, Trait, LoadTests, IEnumerable (+10 more)

### Community 15 - "Solution Project Structure"
Cohesion: 0.07
Nodes (31): FormID Database Manager.Core, net10.0, Microsoft.Data.Sqlite (10.0.0), Mutagen.Bethesda (0.51.5), Microsoft.NET.Sdk, FormID Database Manager.Tests, net10.0, Microsoft.Data.Sqlite (10.0.0) (+23 more)

### Community 16 - "Main Window XAML"
Cohesion: 0.07
Nodes (30): AdvancedMode, AvailableGames, DatabasePath, DetectedDirectories, ErrorMessages, FilteredPlugins, FormIdListPath, GameDirectory (+22 more)

### Community 17 - "Platform Boundary Tests"
Cohesion: 0.14
Nodes (7): FormID_Database_Manager.Tests.Unit.Architecture, Theory, TestCollectionDefinitionTests, Fact, WinUiPlatformServiceSourceTests, MemberData, TheoryData

### Community 18 - "Database Benchmarks"
Cohesion: 0.15
Nodes (13): Benchmark, entry, formid, GameRelease, GlobalCleanup, GlobalSetup, List, plugin (+5 more)

### Community 19 - "Database Integration Tests"
Cohesion: 0.24
Nodes (11): entry, Fact, formid, GameRelease, IEnumerable, List, plugin, SqliteConnection (+3 more)

### Community 20 - "Service Test Namespaces"
Cohesion: 0.15
Nodes (5): FormID_Database_Manager.TestUtilities.Builders, FormID_Database_Manager.Tests.Unit.Services, FormID_Database_Manager.Tests.Integration, FormID_Database_Manager.TestUtilities.Mocks, FormID_Database_Manager.Services

### Community 21 - "Performance Regression Tests"
Cohesion: 0.17
Nodes (14): Dictionary, editorId, formId, InlineData, IReadOnlyList, ITestOutputHelper, List, ManualPerformanceFact (+6 more)

### Community 23 - "Plugin Ingestion Core"
Cohesion: 0.14
Nodes (16): BinaryReadParameters, CancellationToken, Exception, GameRelease, IEnumerable, IMajorRecordGetter, IModDisposeGetter, IProgress (+8 more)

### Community 24 - "ViewModel Collections"
Cohesion: 0.15
Nodes (10): double, bool, CancellationTokenSource, int, Lock, string, MainWindowViewModel, NotifyCollectionChangedEventArgs (+2 more)

### Community 25 - "Test Environment Attributes"
Cohesion: 0.13
Nodes (11): FactAttribute, GameRelease, ExpectsGameEnvironmentFailureFactAttribute, string, ManualPerformanceFactAttribute, ManualPerformanceSkip, ManualPerformanceTheoryAttribute, GameRelease (+3 more)

### Community 26 - "Discovery Result Model"
Cohesion: 0.15
Nodes (10): PluginListDiscoveryCompleted, PluginListDiscoveryFailed, PluginListDiscoveryResult, CancellationToken, Exception, IProgress, TaskCompletionSource, DeterministicPluginListDiscovery (+2 more)

### Community 27 - "User Workflow Service"
Cohesion: 0.27
Nodes (7): bool, GameRelease, int, IReadOnlyList, Task, GameContextSnapshot, UserWorkflow

### Community 29 - "Graphify Tooling"
Cohesion: 0.12
Nodes (18): URL Ingestion and Folder Watch, Wiki, Database, Vector, and Visualization Exports, Semantic Extraction Contract, Semantic Similarity and Hyperedges, GitHub Clone and Cross-Repository Merge, Post-Commit Graph Refresh, BFS and DFS Graph Traversal, Constrained Query Expansion (+10 more)

### Community 30 - "Main Window Events"
Cohesion: 0.16
Nodes (5): bool, RequiresUnreferencedCode, MainWindow, RoutedEventArgs, SelectionChangedEventArgs

### Community 31 - "Memory Benchmarks"
Cohesion: 0.17
Nodes (9): Benchmark, GlobalCleanup, GlobalSetup, IReadOnlyList, string, Task, MemoryBenchmarks, MemoryConfig (+1 more)

### Community 32 - "Controlled Discovery Fakes"
Cohesion: 0.15
Nodes (9): CancellationToken, Exception, int, IProgress, Queue, TaskCompletionSource, ControlledPluginListDiscovery, OvertakingPluginListDiscovery (+1 more)

### Community 33 - "Issue Triage Governance"
Cohesion: 0.14
Nodes (16): Fetch Ticket Means View GitHub Issue, GitHub CLI Issue Operations, GitHub Issues Tracker, Issue Tracker: GitHub, Issues-Only Triage Surface, evildarkarchon/FormID-Database-Manager, Publish Means Create a GitHub Issue, Shared Issue and Pull Request Number Space (+8 more)

### Community 34 - "Store Opener Concurrency"
Cohesion: 0.17
Nodes (11): CancellationToken, GameRelease, Task, FormIdRecordStoreSessionOpener, IFormIdRecordStoreSessionOpener, int, TaskCompletionSource, BlockingPluginIngestion (+3 more)

### Community 35 - "Presentation Adapter Outcomes"
Cohesion: 0.31
Nodes (4): IEnumerable, Fact, Task, PluginListPresentationAdapterTests

### Community 36 - "Discovery Behavioral Tests"
Cohesion: 0.20
Nodes (8): Fact, InlineData, string, Task, Theory, PluginListDiscoveryTests, ThrowingProgress, GameLoadOrderSnapshotFactory

### Community 37 - "Performance Test Projects"
Cohesion: 0.15
Nodes (6): FormID_Database_Manager.Tests.Unit.TestUtilities, FormID_Database_Manager.TestUtilities, FormID_Database_Manager.Tests.Performance, PerformanceProcessingRunFactory, GameRelease, GameInstallationHelper

### Community 38 - "Architecture Boundary Tests"
Cohesion: 0.27
Nodes (4): Fact, InlineData, Theory, CoreProjectBoundaryTests

### Community 40 - "ViewModel UI Bindings"
Cohesion: 0.20
Nodes (5): FormID_Database_Manager.Tests.Unit.ViewModels, FormID_Database_Manager.WinUI.Services, FormID_Database_Manager.ViewModels, FormID_Database_Manager.Tests.UI, FormID_Database_Manager.Models

### Community 41 - "Plugin List Presentation"
Cohesion: 0.18
Nodes (8): bool, string, PluginListItem, IEnumerable, Fact, DataBindingTests, IDataErrorInfo, ObservableObject

### Community 42 - "Ingestion Outcome Contracts"
Cohesion: 0.22
Nodes (11): IEnumerable, FailedPlugin, FailedPluginReason, IngestedPlugin, PluginIngestionOutcome, PluginIngestionProgressStage, PluginReadDiagnostic, PluginReadPhase (+3 more)

### Community 43 - "Discovery Test Doubles"
Cohesion: 0.15
Nodes (10): DiscoveryStep, CancellationToken, IProgress, Task, IPluginListDiscovery, CancellationToken, IProgress, DeterministicPluginListDiscovery (+2 more)

### Community 44 - "Game Load Order Provider"
Cohesion: 0.26
Nodes (6): Func, GameRelease, IReadOnlyList, GameLoadOrderProvider, Fact, GameLoadOrderProviderTests

### Community 45 - "Plugin List Source"
Cohesion: 0.19
Nodes (9): GameRelease, PluginListSource, CancellationToken, Func, IProgress, IReadOnlyList, RecordingPluginListDiscovery, IEquatable (+1 more)

### Community 46 - "Entry Extraction"
Cohesion: 0.29
Nodes (7): ConcurrentDictionary, Action, Exception, HashSet, IMajorRecordGetter, RequiresUnreferencedCode, EntryExtraction

### Community 48 - "ViewModel Processing Commands"
Cohesion: 0.18
Nodes (3): RequiresUnreferencedCode, GameContextSnapshot, WindowEventArgs

### Community 49 - "Installation Attribute Tests"
Cohesion: 0.29
Nodes (4): Fact, Func, GameRelease, GameInstallationAttributeTests

### Community 50 - "xUnit Runner Configuration"
Cohesion: 0.17
Nodes (11): diagnosticMessages, internalDiagnosticMessages, longRunningTestSeconds, maxParallelThreads, methodDisplay, methodDisplayOptions, parallelizeAssembly, parallelizeTestCollections (+3 more)

### Community 51 - "Load Order Abstraction"
Cohesion: 0.24
Nodes (6): GameRelease, IReadOnlyList, IGameLoadOrderProvider, GameRelease, IReadOnlyList, StaticGameLoadOrderProvider

### Community 52 - "Plugin List Discovery"
Cohesion: 0.33
Nodes (6): DiscoveryProgress, PluginListDiscoveryProgress, CancellationToken, IProgress, Task, PluginListDiscovery

### Community 53 - "Game Context Projection"
Cohesion: 0.24
Nodes (3): AdvancedMode, GameRelease, IReadOnlyList

### Community 54 - "Default Database Path"
Cohesion: 0.29
Nodes (5): GameRelease, string, DefaultDatabasePathProvider, Fact, DefaultDatabasePathProviderTests

### Community 55 - "Store Session Test Doubles"
Cohesion: 0.20
Nodes (6): Action, Exception, IReadOnlyList, ValueTask, CancellationAwareRecordStoreSession, RecordingRecordStoreSession

### Community 56 - "Test Mock Factory"
Cohesion: 0.29
Nodes (5): Action, CancellationTokenSource, List, Mock, MockFactory

### Community 57 - "Recording Thread Dispatcher"
Cohesion: 0.31
Nodes (4): Action, bool, ConcurrentQueue, RecordingThreadDispatcher

### Community 58 - "Load Order Snapshot"
Cohesion: 0.36
Nodes (5): BinaryReadParameters, HashSet, IReadOnlyList, GameLoadOrderSnapshot, IModMasterStyledGetter

### Community 59 - "Packaging and Distribution"
Cohesion: 0.29
Nodes (7): Portable Windows x64 Artifact, Windows Build and Test CI, GNU General Public License Version 3, FormID Database Manager, Portable Self-Contained WinUI Distribution, Supported Bethesda Games, WinUI Migration Checkpoint

### Community 60 - "WinUI Application Startup"
Cohesion: 0.29
Nodes (4): FormID_Database_Manager.WinUI, Application, App, LaunchActivatedEventArgs

### Community 61 - "Processing Warning Collection"
Cohesion: 0.29
Nodes (5): int, List, RecordWarningCollector, int, ProcessingWarning

### Community 62 - "Test Collections"
Cohesion: 0.33
Nodes (5): FormID_Database_Manager.Tests, DatabaseTestCollection, IntegrationTestCollection, PerformanceTestCollection, UiTestCollection

### Community 63 - "Sequenced Discovery"
Cohesion: 0.33
Nodes (5): CancellationToken, int, IProgress, IReadOnlyList, SequencedPluginListDiscovery

### Community 64 - "Game Location Service"
Cohesion: 0.40
Nodes (3): GameRelease, List, GameLocationService

### Community 65 - "Game Location Contract"
Cohesion: 0.40
Nodes (3): GameRelease, List, IGameLocationService

## Ambiguous Edges - Review These
- `FormID List Test Data` → `Test Data Management`  [AMBIGUOUS]
  docs/Testing-Best-Practices.md · relation: shares_data_with

## Knowledge Gaps
- **108 isolated node(s):** `net10.0`, `CommunityToolkit.Mvvm (8.4.2)`, `Microsoft.Data.Sqlite (10.0.0)`, `Mutagen.Bethesda (0.51.5)`, `Microsoft.NET.Sdk` (+103 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **6 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `FormID List Test Data` and `Test Data Management`?**
  _Edge tagged AMBIGUOUS (relation: shares_data_with) - confidence is low._
- **Why does `FormID_Database_Manager.Services` connect `Service Test Namespaces` to `Plugin List Refresh`, `Plugin Ingestion Tests`, `Processing Run Execution`, `Game Detection`, `User Workflow Tests`, `Thread Dispatching`, `Store Domain Records`, `Plugin Ingestion Core`, `Store Opener Concurrency`, `Performance Test Projects`, `ViewModel UI Bindings`, `Ingestion Outcome Contracts`, `Game Load Order Provider`, `Plugin List Source`, `Entry Extraction`, `File Dialog Selection`, `Load Order Abstraction`, `Plugin List Discovery`, `Default Database Path`, `Load Order Snapshot`, `Game Location Service`, `Game Location Contract`?**
  _High betweenness centrality (0.218) - this node is a cross-community bridge._
- **Why does `MainWindowViewModel` connect `ViewModel Collections` to `Plugin List Refresh`, `User Workflow Tests`, `Record Store Opening`, `Thread Dispatching`, `ViewModel Concurrency Tests`, `ViewModel UI Bindings`, `Plugin List Presentation`, `Main ViewModel Tests`, `ViewModel Processing Commands`, `Game Context Projection`, `ViewModel Message Dispatch`, `User Workflow Service`, `Main Window Events`?**
  _High betweenness centrality (0.120) - this node is a cross-community bridge._
- **Why does `MainWindowViewModelTests` connect `Main ViewModel Tests` to `Plugin Name Validation`, `Thread Dispatching`, `ViewModel Concurrency Tests`, `ViewModel UI Bindings`, `Game Context Projection`, `ViewModel Message Dispatch`, `ViewModel Collections`?**
  _High betweenness centrality (0.064) - this node is a cross-community bridge._
- **What connects `net10.0`, `CommunityToolkit.Mvvm (8.4.2)`, `Microsoft.Data.Sqlite (10.0.0)` to the rest of the system?**
  _108 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Plugin List Refresh` be split into smaller, more focused modules?**
  _Cohesion score 0.0547680412371134 - nodes in this community are weakly interconnected._
- **Should `Plugin Ingestion Tests` be split into smaller, more focused modules?**
  _Cohesion score 0.05958485958485959 - nodes in this community are weakly interconnected._
