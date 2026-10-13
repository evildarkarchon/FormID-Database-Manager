# ADR-0009: The Plugin List publishes one coherent, revisioned state

- Status: Accepted
- Date: 2026-10-13 (records the design accepted in issue #70 and implemented by #71 and #72, August 2026)

## Context

The Plugin List's state contract once relied on object identity to tell the presentation adapter whether an activity
was new: a selection-only update reused the same activity object so the adapter would not present the same status
twice. That hidden identity contract was fragile — a routine refactor that re-allocated a record could replay status
messages — and the permissive state-plus-activity shape could represent combinations that should never exist, such as
Ready without a Confirmed Plugin List, or a confirmation belonging to a different Plugin List Source.

Issue #70 resolved this through a grilling and design comparison, but recorded the outcome only in the issue. An
architecture review on 2026-10-13 then re-proposed reversing two of its decisions — splitting activity from
confirmation, and replacing the numeric revisions with something the adapter would not have to track — because nothing
in the repository said they had been chosen deliberately. This ADR is that record.

## Decision

**The Plugin List publishes one immutable `Current` snapshot through a signal-only `Changed` event, as one closed
hierarchy of coherent state variants carrying two explicit numeric revisions.**

- One snapshot, not separate state and activity streams. The variants are No Source, Refreshing, Ready, Failed,
  Cancelled, and Faulted, and consumers handle that complete space directly.
- The variants make incoherent combinations unconstructable: Ready requires a Confirmed Plugin List and derives its
  source and membership version from it; No Source carries none; every other sourced variant may retain a confirmation
  only for the same Plugin List Source, which a shared sourced-state base validates.
- **State Revision** advances on every publication and drives membership projection. **Activity Revision** advances on
  every genuine activity occurrence — refresh start, progress, readiness, failure, cancellation, fault, invalidation —
  including one structurally equal to an earlier occurrence. A selection-only publication advances State Revision and
  preserves Activity Revision, which is the explicit reason the adapter stays silent on selection changes.
- Both revisions are plain `long` values on the public state contract, not wrapped and not inferred from object
  identity. Activity freshness is data, so correctness does not depend on reference equality or allocation behavior.
- The Plugin List alone constructs states. Tests drive commands and observe `Current`, and do not assemble variants.

## Considered options

- **Object identity for activity freshness** was the prior design, rejected because it made correctness depend on
  record allocation, which nothing in the type system or the tests protected.
- **Separate state and activity streams** were rejected because the adapter must project membership before activity
  from one consistent read, and two streams reopen the ordering question the single snapshot answers.
- **A permissive record with a nested activity and an optional confirmation** was rejected because it pushes coherence
  checks onto every consumer.
- **An opaque one-field revision wrapper** was rejected as interface without behavior.

## Consequences

- Plugin List tests assert revision arithmetic deliberately; those assertions pin this contract and are not incidental
  implementation detail.
- The presentation adapter remembers the last presented State Revision and Activity Revision, and can be exercised only
  through a real Plugin List, since states are not constructible outside it.
- Internal refactors of the Plugin List — how it computes the next state, retains same-source confirmation, or
  publishes a selection — are free to change provided this contract and its tests are unchanged.
