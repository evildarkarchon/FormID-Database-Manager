Status: ready-for-agent

# Give the Plugin List's publication rules one home each

## Problem Statement

The Plugin List's interface is settled and deliberate (ADR-0009): one immutable `Current` snapshot, a signal-only
`Changed` event, a closed hierarchy of coherent state variants, and explicit State Revision and Activity Revision
values. This work does not change that interface.

Its implementation, however, states the same rules in several places. An architecture review on 2026-10-13 found:

- The rule that a Confirmed Plugin List survives only for the same Plugin List Source is computed independently in
  `BeginRefresh` (`PluginList.cs:349`), `PublishCompleted` (`:436`) and `PublishTerminal` (`:548`), and assumed without
  a check in `PublishProgress` (`:572`).
- Progress, completion, and the three non-success terminals each repeat the same skeleton: take the publication gate,
  check that the refresh generation is still current, decide which confirmation survives, release active ownership if
  terminal, publish an activity occurrence, and raise `Changed` after the gate is released. Only the three terminals
  share it today, through `PublishTerminal`.
- A selection-only publication rebuilds the current variant through a six-way switch in `PublishSelectionLocked`
  (`:607-650`) whose `default` arm throws at runtime, so a new variant that forgets selection fails only when a user
  selects a Plugin in that state.
- Selection-only publication from the Failed, Cancelled, and Faulted variants (each holding a retained same-source
  confirmation) is not covered by any test; only the Refreshing case is (`PluginListTests.cs:500-541`).

Separately, User Workflow request creation re-creates a `PluginListSource` from the captured Game Context solely to
read its Data directory (`UserWorkflow.cs:742-744`), a fact the matched Confirmed Plugin List already carries.

## Solution

Refactor the Plugin List's implementation so each rule has one home, without changing its interface, its observable
behavior, or its existing tests:

- One private helper answers which confirmation survives for a given Plugin List Source.
- One private guarded path publishes every refresh-driven activity occurrence (progress, completion, failure,
  cancellation, fault).
- Each sourced state variant knows how to re-issue itself with a new selection, replacing the switch, so the compiler
  rather than a runtime `default` arm enforces that every variant supports selection.

Pin the currently untested selection behavior with characterization tests before refactoring. Separately, let User
Workflow request creation read the Data directory from the matched Confirmed Plugin List.

## User Stories

1. As an application user, I want Plugin List refresh, selection, cancellation, and failure behavior unchanged, so that
   an internal refactor does not disturb my workflow.
2. As an application user, I want selecting Plugins after a failed, cancelled, or faulted same-source refresh to keep
   working exactly as it does today, so that a failed retry does not stop me from adjusting my selection.
3. As an application user, I want status messages neither duplicated nor lost by this change, so that what I read still
   matches what the Plugin List did.
4. As a maintainer, I want the same-source confirmation-retention rule stated once, so that changing it is one edit
   rather than four.
5. As a maintainer, I want refresh-driven publications to share one guarded path, so that the generation check,
   ownership release, and signal timing cannot drift between them.
6. As a maintainer, I want every sourced state variant to define its own selection-only re-issue, so that adding a
   variant without selection support fails at compile time.
7. As a maintainer, I want the retirement and signal ordering in `BeginRefresh` and `Invalidate` left exactly as they
   are, so that their documented race handling is not disturbed by a locality refactor.
8. As a test author, I want selection-only publication from every sourced non-Ready variant pinned before the
   refactor, so that replacing the switch is proven behavior-preserving.
9. As a test author, I want all existing Plugin List and presentation-adapter tests to pass unchanged, so that they
   serve as the regression net for ADR-0009's contract.
10. As a User Workflow maintainer, I want the run request's Data directory to come from the Confirmed Plugin List that
    supplied its selected Plugins, so that the request's directory and selection share one source.

## Implementation Decisions

- ADR-0009 governs. `Current`, `Changed`, the six variants, both revisions, their advancement rules, and command-driven
  testing are unchanged. No variant is added, removed, or renamed, and no state or activity split is introduced.
- A single private helper on `PluginList` computes the confirmation retained for a Plugin List Source: the current
  confirmation when its source equals the given source, otherwise none. `BeginRefresh`, progress publication,
  completion's selection reconciliation, and the three terminals all use it.
- Progress publication uses that helper instead of retaining `_current.Confirmed` unconditionally. While
  `BeginRefresh` establishes the invariant the results are identical; the sourced-state constructor remains the
  backstop for any path that supplies a confirmation directly.
- One private guarded path publishes refresh-driven activity: it takes the gate, returns without publishing unless the
  operation is the current live generation, computes the retained confirmation, clears active refresh ownership for
  terminal publications, publishes an activity occurrence (advancing both revisions), and raises `Changed` after
  releasing the gate. Progress, completion, failure, cancellation, and fault use it; `PublishTerminal` is absorbed.
- Completion's membership rules (Base Game Plugin hiding under Advanced Mode Off, first case-insensitive occurrence
  owning position and casing) still run outside the gate, and its selection reconciliation still runs under the gate so
  selection applied during discovery participates in the commit.
- `BeginRefresh` and `Invalidate` keep their own publication code and their exact retirement and signal ordering,
  including `Invalidate` signalling `Changed` even when retirement throws, and `BeginRefresh` publishing Faulted and
  releasing its operation when retirement or signalling fails. They change the generation rather than check it, so the
  guarded path does not apply to them. They use the shared retention helper and the existing activity primitive.
- `SourcedPluginListState` gains an abstract `internal` member that returns the same concrete variant with a supplied
  non-null `ConfirmedPluginList` and a supplied State Revision, preserving its Activity Revision and its own data
  (scan counts, error message). Every sourced variant implements it through its validating constructor, not through a
  record `with` expression, so the same-source check is never bypassed.
- The member sits on `SourcedPluginListState`, not `PluginListState`, so No Source needs no throwing implementation.
  `Apply` reaches it through a sourced-state pattern match, which always holds whenever `Confirmed` is non-null.
- The member is `internal`, the same visibility as the existing constructors, so it does not widen who can construct
  states; ADR-0009's "Plugin List alone constructs states" continues to hold by convention as it does today.
- `PublishSelectionLocked`'s switch is deleted. Selection publication still advances only State Revision.
- `Apply`'s rejection rules (no confirmation, stale membership version, unknown name, already satisfied) and its
  publish-nothing behavior on rejection are unchanged.
- `RefreshOperation`, its deferred-dispose lifetime, generation numbering, and disposal behavior are unchanged.
- User Workflow request creation builds the selected-Plugin request's game directory from the matched Confirmed Plugin
  List's `Source.DataDirectory` instead of calling `PluginListSource.Create` a second time. The capture-time match
  `GetConfirmedPluginListFor` stays in User Workflow: Browse can store a new directory and be overtaken before it
  invalidates the Plugin List, so the match is load-bearing, and moving a one-expression comparison behind a Plugin
  List member would be a pass-through.
- No glossary term changes. No new ADR beyond ADR-0009, which this work relies on.

## Testing Decisions

- The Plugin List interface is the test surface: tests issue commands and observe `Current` and `Changed`, and never
  construct state variants (ADR-0009).
- Before any production change, add characterization tests for selection-only publication from Failed, Cancelled, and
  Faulted, each reached through commands with a retained same-source confirmation. Each asserts that the concrete
  variant is unchanged, Activity Revision is unchanged, State Revision advances by one, the variant's own data survives
  (error message for Failed), and the new selection is visible in `Confirmed`. These must pass against current code.
- If the existing Refreshing selection test does not already assert that scan counts survive and Activity Revision is
  preserved, extend it in the same way.
- Existing `PluginListTests`, `PluginListPresentationAdapterTests`, and `UserWorkflowTests` pass without modification;
  any needed edit to an existing assertion means behavior changed and must be justified in the issue's completion note.
- `TypeShape_StateHierarchy_HasOnlyConcreteCoherentVariants` keeps pinning the variant set; it is not relaxed.
- `GameLoadOrdersArchitectureTests.LiveCallers_GameLoadOrdersCutover_UseHighestSeamWithoutForwardingDiscoveryLayer`
  reads `PluginList.cs` source text and requires `IGameLoadOrders` and `DiscoverAvailablePluginsAsync` to appear; keep
  both.
- No new test seam, no reflection over private members, no pure transition function tested directly.
- The supported build sequence and the non-performance test suite must pass.

## Out of Scope

- Any change to ADR-0009's interface: the snapshot shape, the variants, either revision, or the `Changed` contract.
- The presentation adapter's revision tracking, dispatch, and wording.
- Advanced Mode as part of the Plugin List Source, and whether turning Advanced Mode off should drop selected Base Game
  Plugins.
- Select All and Select None applying to Plugins hidden by the text filter (intended); its labelling is tracked in
  `.scratch/select-all-filter-label/`.
- Re-syncing a checkbox after a rejected selection intent.
- Moving Processing Run coordination out of User Workflow, or WinUI composition and lifetime changes.
- `RefreshOperation` lifetime and cancellation handling.

## Further Notes

- This is a behavior-preserving, in-process deepening of implementation locality. The interface stays as ADR-0009
  records it; the review that prompted this work had initially proposed changing that interface, and ADR-0009 was
  written to stop that recurring.
- Preserve accurate comments. Where comments in `BeginRefresh`, `PublishCompleted`, `PublishTerminal`, or
  `PublishProgress` describe logic that moves into the shared helper or guarded path, move them with it; rewrite one
  only if it becomes misleading, and say so in the completion note.
- Add doc comments to the new helper, the guarded path, and the new abstract member and its overrides.
