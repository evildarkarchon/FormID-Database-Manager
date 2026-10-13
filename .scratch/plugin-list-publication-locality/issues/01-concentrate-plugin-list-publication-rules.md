# 01: Concentrate the Plugin List's publication rules

**What to build:** Refactor `PluginList` and its state variants so the same-source confirmation-retention rule, the
refresh-driven publication skeleton, and selection-only re-issue each have one home, without changing the interface
ADR-0009 records or any observable behavior. See `../spec.md`.

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

- [ ] Characterization tests are added first, through Plugin List commands only, for selection-only publication from
      Failed, Cancelled, and Faulted states holding a retained same-source confirmation. Each asserts the variant and
      Activity Revision are unchanged, State Revision advances by one, the variant's own data survives, and the new
      selection is visible. They pass against the unmodified implementation.
- [ ] The existing Refreshing selection test asserts scan counts survive and Activity Revision is preserved, extended
      if it does not already.
- [ ] One private helper decides which confirmation survives for a Plugin List Source; `BeginRefresh`, progress,
      completion reconciliation, and all three terminals use it, and no other code compares a confirmation's source.
- [ ] One private guarded path publishes progress, completion, failure, cancellation, and fault: gate, live-generation
      check, retained confirmation, terminal ownership release, activity publication, `Changed` raised after the gate.
      `PublishTerminal` no longer exists as a separate skeleton.
- [ ] `BeginRefresh` and `Invalidate` keep their exact retirement, fault-on-failure, and signal ordering.
- [ ] `SourcedPluginListState` declares an abstract `internal` selection re-issue member; Refreshing, Ready, Failed,
      Cancelled, and Faulted implement it through their validating constructors, preserving Activity Revision and
      their own data. No Source does not implement it.
- [ ] `PublishSelectionLocked`'s six-way switch is deleted; `Apply` reaches the member through a sourced-state match.
- [ ] `Apply` rejection rules, `RefreshOperation` lifetime, generation numbering, and disposal are unchanged.
- [ ] All existing `PluginListTests`, `PluginListPresentationAdapterTests`, `UserWorkflowTests`, and architecture
      tests pass without modification.
- [ ] Accurate comments move with the logic they describe; any rewritten or removed comment is listed in the
      completion note. New members carry doc comments.
- [ ] The supported build sequence and the non-performance test suite pass.
