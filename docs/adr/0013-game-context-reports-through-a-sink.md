# ADR-0013: Game Context reports through a sink, and Game Context Presentation renders its notices

- Status: Accepted
- Date: 2026-10-19

## Context

ADR-0011 made Game Context its own module and kept "its own ViewModel projection and messages" inside it. It
considered publishing typed state and notices through a separate adapter, and rejected that "for now: there would be
one adapter, so the seam would be hypothetical."

One day later, ADR-0012 put Processing Run Control behind a sink and counted a recording test sink as the second
adapter: "Tests implement it by recording, so the seam has two adapters." The two ADRs applied different seam rules to
the same shape, a Core module whose output reaches the ViewModel.

Game Context writes the concrete `MainWindowViewModel` in five places: one projection call and four messages, whose
wording is built inside `GameContext.cs`. As a result:

- `GameContextTests` construct a real ViewModel and make 109 assertions on its properties and message lists, not on
  anything Game Context itself returns or reports.
- Game Context's messages are sentences, so its tests restate wording.
- The ViewModel has three Core writers: Game Context, the Plugin List Presentation Adapter, and the User Workflow's run
  sink adapter.

## Decision

**A seam is real when it has two adapters, and a recording test adapter counts as one when the module's tests assert
through it in place of a heavier real collaborator.** This is the rule ADR-0012 already relied on. Under it, ADR-0011's
reason for rejecting a separate projection no longer holds.

**Game Context reports to a sink and writes nothing.**

- **The sink.** `GameContext` takes an `IGameContextSink` instead of `MainWindowViewModel`. The sink receives two
  things:
  - the complete `GameContextState`, on every publication;
  - a typed Game Context notice.
- **The notices.** There are four, as a closed set of value records:
  - the picker failed, with its error text;
  - no installed locations were found for a GameRelease;
  - a directory was refused, with the failure's message as a string;
  - no game was detected in the directory.
- **Ordering and threading.**
  - Game Context writes its state before reporting it, so a re-entrant ComboBox echo reads the new state and stays a
    no-op.
  - A report can re-enter and start a newer intent, so callers keep their generation checks after it returns.
  - The sink is UI-thread-affine like the rest of Game Context, and no lock is held. Processing Run Control needs its
    lock for the #60 ordering, and Game Context has no equivalent.
- **No wording.** `GameContext.cs` contains no user-facing wording.

**Game Context Presentation renders the notices.**

- A pure static `GameContextPresentation.Render` returns the text and the message list it belongs to.
- Which list a notice goes to is a presentation choice, so it sits next to the wording.

**The User Workflow writes the ViewModel.**

- A private nested `ViewModelGameContextSink` in `UserWorkflow` maps each reported state onto the ViewModel's Game
  Context projection, and writes each rendered notice to its list.
- It is separate from the run sink adapter, because the two sinks have different ordering contracts.

This supersedes three parts of ADR-0011:

- that `GameContext` owns "its own ViewModel projection and messages";
- that `UserWorkflow` constructs `GameContext` from the ViewModel;
- the rejection of a separate projection because there would be one adapter.

The rest of ADR-0011 stands:

- Game Context owns its state, its four transitions, the generation counter, and the Plugin List Source;
- it borrows the Plugin List and drives its refresh and invalidation directly;
- sources, not strings, cross the seam.

## Considered options

- **Keep the ViewModel in Game Context.** Rejected. Its tests stay coupled to the ViewModel's shape, and the ViewModel
  keeps a Core writer outside the User Workflow.
- **Mirror the run sink literally, with rendered error and information strings.** Rejected. ADR-0012 sent rendered
  strings only because run activity carries a rendered percentage forward, so rendering has to happen where that
  activity is held. Game Context holds no rendered state.
- **Render the notices in the User Workflow's adapter, or on the notice records.** Rejected. The first puts wording
  into `UserWorkflow`, which has none. The second puts it back beside Game Context.
- **A sink with the ViewModel's four projected fields.** Rejected. It copies the ViewModel's shape into Game Context's
  interface. With the whole state, a recording sink sees every publication in order.
- **Also return the Plugin List refresh to the caller, to free Game Context's tests from a real Plugin List.**
  Rejected again, for ADR-0011's reason: the refresh is valid only while its generation is the latest.

## Consequences

- `GameContextTests` record states and notices through a recording sink. They keep the real Plugin List, the in-memory
  installation probe, and the scripted load orders.
- `UserWorkflowTests` cover the adapter: a reported state reaches the projection, and each notice reaches the list
  `GameContextPresentation` chooses, with the text it renders.
- Game Context wording is asserted only in `GameContextPresentationTests`.
- Architecture tests change:
  - the Core writers of the Game Context projection become `MainWindowViewModel.cs` and `UserWorkflow.cs`;
  - `GameContext.cs` references neither `MainWindowViewModel` nor the ViewModels namespace;
  - `GameContext.cs` has a closed set of string literals, as `ProcessingRunControl.cs` does.
- WinUI keeps its `InternalsVisibleTo` access to Core. Closing that access is a separate question about the shell's
  boundary.
- This ADR records the decision ahead of the change.
