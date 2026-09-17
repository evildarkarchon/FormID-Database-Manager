Status: ready-for-agent

# Make selected-Plugin facts authoritative once

## Problem Statement

A selected-Plugin Processing Run currently captures the same game directory, GameRelease, ordered Plugin names, and Update Mode in both its Processing Run request and a second Plugin Ingestion request. Processing Run then reconstructs the second value before both Dry Run planning and real Plugin Ingestion.

This duplication makes it possible for planning and ingestion to drift, repeats immutable selection capture and validation, and forces maintainers and tests to understand a field-for-field translation that adds no behavior. A user should be able to trust that the exact selected Plugins accepted for a Processing Run are the Plugins planned or ingested, in the same casing and order, without a second request being reconstructed internally.

## Solution

Make the selected-Plugin Processing Run request the single authoritative value for selected-Plugin facts. Processing Run will pass that same immutable request instance directly to Plugin Ingestion for both planning and ingestion.

The existing public construction and validation contract will remain intact. Plugin Ingestion plans and reports will validate that they contain exactly one correctly ordered entry for every selected Plugin, but they will not retain unrelated Processing Run facts after construction. The duplicate internal request, translation logic, and standalone selection-capture helper will be removed.

## User Stories

1. As a user starting a selected-Plugin Processing Run, I want the accepted Plugin selection to be captured once, so that later processing cannot silently use a different selection.
2. As a user running a Dry Run, I want the plan to correspond exactly to my selected Plugins, so that every planned entry maps to the selection I submitted.
3. As a user running a real ingestion, I want its outcomes to correspond exactly to my selected Plugins, so that the final report cannot omit, add, rename, or reorder Plugins.
4. As a user, I want Plugin casing preserved from my selection, so that reports and stored results use the names I saw when starting the run.
5. As a user, I want Plugin order preserved from the Confirmed Plugin List selection, so that planning and ingestion proceed predictably.
6. As a user, I want Plugin names compared case-insensitively for uniqueness, so that casing variants cannot cause the same Plugin to be processed twice.
7. As a user, I want an empty Plugin selection rejected before processing begins, so that the application reports the existing actionable validation message.
8. As a user, I want blank Plugin names rejected before processing begins, so that malformed selection data cannot reach Plugin Ingestion.
9. As a user, I want a blank game directory rejected before processing begins, so that Plugin Ingestion never starts without a resolvable location.
10. As a user, I want non-Dry Runs to continue requiring a FormID Record Store path, so that this refactor does not weaken Store validation.
11. As a Dry Run user, I want a Store path to remain optional, so that planning continues to open no FormID Record Store.
12. As a user, I want existing validation wording preserved, so that this architecture change causes no unexplained user-facing message changes.
13. As a user, I want existing validation precedence preserved when several inputs are invalid, so that the first reported problem remains stable.
14. As a caller constructing a selected-Plugin request, I want the existing public constructor and properties to remain available, so that the change does not require unrelated caller migration.
15. As a caller, I want to continue constructing a request from explicit game directory, GameRelease, Plugin names, Update Mode, Store path, and Dry Run facts, so that request creation is not restricted to User Workflow.
16. As a Processing Run maintainer, I want Dry Run planning and real ingestion to receive the same request instance, so that no translation can drift between the two paths.
17. As a Plugin Ingestion maintainer, I want selection validation and immutable capture owned by the request, so that Plugin Ingestion can assume its selected-Plugin facts are coherent.
18. As a Plugin Ingestion maintainer, I want planning versus ingestion expressed by the operation invoked, so that Plugin Ingestion does not duplicate Processing Run's Dry Run routing policy.
19. As a maintainer, I want the duplicate selected-Plugin request removed rather than retained as a compatibility shim, so that the architecture has one authority instead of two names for the same facts.
20. As a maintainer, I want the standalone selection-capture helper removed when it has only one caller, so that selection invariants have locality inside the authoritative request module.
21. As a maintainer, I want Plugin Ingestion plans and reports to validate their relationship to the authoritative selection, so that invalid aggregate values cannot be constructed.
22. As a maintainer, I want plans and reports to discard the request after validating their entries, so that their interfaces expose only their own relevant facts.
23. As a test author, I want contract tests to exercise the same request interface production uses, so that tests survive internal implementation changes.
24. As a test author, I want Processing Run tests to assert request identity at the Plugin Ingestion seam, so that reintroducing a mapper or second snapshot fails the suite.
25. As a test author, I want existing Plugin Ingestion behavior scenarios to remain intact, so that cancellation, overlay lifetime, skip classification, failure classification, and Store behavior remain covered.
26. As a future coding agent, I want one obvious selected-Plugin authority, so that I can change selection rules without navigating duplicate request modules.

## Implementation Decisions

- `PluginProcessingRunRequest` is the sole authoritative module for the selected-Plugin facts used by a Processing Run.
- The existing public constructor and public properties remain source-compatible.
- The request continues to capture Plugin names eagerly into an immutable ordered snapshot.
- The request owns the non-empty, nonblank, and case-insensitive uniqueness invariants for selected Plugin names.
- Existing `ProcessingRunValidationException` types, exact user-facing validation wording, and validation precedence remain unchanged.
- Callers may continue constructing a request from explicit facts; a Confirmed Plugin List is not required by the request interface.
- Plugin Ingestion's planning and ingestion operations both accept `PluginProcessingRunRequest` directly.
- Processing Run passes the same request object to Plugin Ingestion. It does not reconstruct or copy selected-Plugin facts at the seam.
- The duplicate internal selected-Plugin request is removed without a compatibility shim.
- The standalone selection-snapshot module is removed. Its behavior becomes private implementation inside the authoritative request module.
- Plugin Ingestion does not validate the request's Dry Run flag. Processing Run remains the owner of choosing planning versus ingestion.
- Plugin Ingestion continues to canonicalize the game-root-or-Data input through Game Installation's existing rule before preparing the Game Load Order.
- Plugin Ingestion continues to read GameRelease and Update Mode from the authoritative request.
- `PluginIngestionReport` accepts the authoritative request during construction and rejects null, missing, extra, renamed, recased, or reordered outcomes.
- `PluginIngestionPlan` accepts the authoritative request during construction and enforces the same cardinality, casing, and ordering relationship for planned entries.
- Plans and reports use the request only for construction-time validation. They do not retain or expose the complete request afterward.
- No new adapter or substitution seam is introduced. The dependency is entirely in-process.
- Plugin Ingestion remains a phase of Processing Run, matching the existing domain glossary.
- The accepted Processing Run outcome and presentation split remains unchanged.
- The accepted Game Load Orders seam, opaque Plugin-read capability, overlay ownership, and Unresolvable Master behavior remain unchanged.
- No new domain term is introduced, so the domain glossary does not need a new entry.
- No new ADR is required because this is a local deepening of interfaces already governed by the existing Processing Run and Game Load Orders decisions.

## Testing Decisions

- Tests should assert behavior through the highest existing seam: the public selected-Plugin Processing Run request and the existing Plugin Ingestion interface.
- Tests should describe observable validation, immutable capture, request identity, plan correlation, report correlation, planning behavior, and ingestion behavior. They should not test private capture helpers or translation implementation.
- The Processing Run contract suite remains the sole coverage for selected-Plugin request validation, immutable capture, casing preservation, order preservation, and exact validation wording.
- Duplicate tests dedicated to constructing the retired internal request are removed.
- Plugin Ingestion contract tests are updated so both planning and ingestion accept the public Processing Run request.
- Plugin Ingestion behavior tests remain at the Plugin Ingestion interface and construct a valid Processing Run request as their input.
- Real-ingestion tests may use a harmless nonblank Store-path value because Plugin Ingestion does not open the Store; Dry Run tests may continue using a blank Store path when the request is marked as Dry Run.
- Processing Run executor tests replace field-by-field translation assertions with object-identity assertions proving that the exact request reaches Plugin Ingestion.
- Processing Run executor tests cover both the Dry Run planning path and the real ingestion path at that identity seam.
- Plugin Ingestion report tests retain their null-entry, cardinality, name, casing, and ordering checks against the authoritative request.
- Plugin Ingestion plan tests gain equivalent cardinality, name, casing, and ordering checks against the authoritative request.
- Reflection-based contract tests require the Plugin Ingestion operations to accept `PluginProcessingRunRequest` and reject reintroduction of the retired internal request.
- Existing tests for cancellation, Unresolvable Master propagation, Plugin-specific failures, Skipped Plugins, overlay disposal, progress, Store writes, optimization, and Processing Run outcomes remain behavioral regression coverage.
- The non-performance test suite and the repository's supported build sequence must pass.
- Prior art is the existing Processing Run contract suite, Plugin Ingestion contract suite, Plugin Ingestion behavior suite, Processing Run executor suite, and architecture tests that pin Core interface shapes.

## Out of Scope

- Unifying the parallel Plugin Ingestion planning and ingestion traversals.
- Changing Game Context transitions or Confirmed Plugin List ownership.
- Requiring a Confirmed Plugin List to construct a selected-Plugin Processing Run request.
- Adding a new public `SelectedPlugins` value or other selected-Plugin interface.
- Changing the public selected-Plugin request constructor or removing any existing public property.
- Adding Dry Run consistency guards inside Plugin Ingestion.
- Moving Data-directory canonicalization away from Game Installation.
- Changing Game Load Orders, the opaque Plugin-read capability, overlay construction, or overlay lifetime ownership.
- Changing FormID Record Store opening, writing, optimization, or disposal.
- Changing Processing Run outcomes, progress facts, presentation wording, or ViewModel writes.
- Changing Plugin selection behavior in Plugin List.
- Refactoring WinUI composition or ownership lifetimes.
- Any user-visible behavior change beyond preventing internal request drift.

## Further Notes

- This specification deepens an existing in-process module; it does not introduce a new seam or adapter.
- The deletion test is the primary architectural justification: removing the duplicate request, mapper, and one-caller helper eliminates complexity rather than redistributing it.
- ADR-0007 remains authoritative for typed Processing Run requests, outcomes, cancellation, and presentation ownership.
- ADR-0008 remains authoritative for the Game Load Orders seam and Plugin-read preparation.
- ADR-0006 remains authoritative for Unresolvable Master classification and propagation.
- The implementation should preserve accurate comments and update doc comments where the request or Plugin Ingestion interfaces are substantially rewritten.
