# ADR-0014: The ViewModel owns the Message Lists, and the sink adapters are modules of their own

- Status: Accepted
- Date: 2026-10-30

## Context

ADR-0012 and ADR-0013 put Processing Run Control and Game Context behind sinks, and placed each sink's ViewModel adapter
in `UserWorkflow` as a private nested class. Neither ADR said who owns the Message Lists the adapters write to. An
architecture review on 2026-10-30 found that nobody does:

- **Five writers, two rules.** Both sink adapters, the Plugin List Presentation Adapter, `UserWorkflow`'s picker
  failures, and the shell's seven "Unexpected error" catches all append to the lists. Appending goes through
  `Add*Message`, which posts to the dispatcher. Clearing goes through two raw `.Clear()` calls in the run sink adapter,
  which do not. That is safe today only because every press arrives on the UI thread. Nothing enforces it.
- **Open lists.** The three lists are `[ObservableProperty]` collections with public setters. Production never replaces
  them, yet six change hooks exist so that a replacement would keep `HasErrorMessages` and its siblings correct.
- **A lock that guards nothing.** `_messagesLock` is taken only after a call has been posted to the UI thread. Every list
  mutation therefore already runs on one thread.
- **Adapters testable only through the whole workflow.** About ten `UserWorkflowTests` exist to check what the two sink
  adapters write. Each builds Game Context, a real Plugin List, admission inputs and a fake executor just to reach a
  one-line forward, because a private nested class has no seam of its own.

## Decision

**The ViewModel owns the Message List rules.**

- A Message List is named by one shared `MessageList` enum: `Error`, `Warning`, `Information`. It replaces
  `GameContextMessageList`, and `GameContextPresentation` returns it.
- Every writer appends through one internal operation, `AddMessage(MessageList, string)`. It posts to the dispatcher when
  called off the UI thread, and keeps a fixed number of the most recent entries (10). The `public virtual` methods, the
  `maxMessages` parameter and `_messagesLock` are retired.
- The start-over rule is one operation, `ClearErrorAndWarningMessages()`, under the same dispatch rule. Processing Run
  Control still decides *when* it happens: on every press that does not cancel a run (ADR-0012). The name says *what*
  happens: information survives.
- The three lists are exposed as `ReadOnlyObservableCollection<string>` over private collections, like `Plugins` and
  `DetectedDirectories`. The replacement hooks are deleted.

**The sink adapters are top-level internal classes that `UserWorkflow` composes.** This supersedes ADR-0012's "implements
the sink over the ViewModel as a private nested adapter" and ADR-0013's "a private nested `ViewModelGameContextSink` in
`UserWorkflow`".

- `ViewModelGameContextSink` and `ViewModelProcessingRunControlSink` keep their names and each moves to its own file in
  `Core/Services`, as `internal sealed`.
- They stay two classes, because their ordering contracts differ: the run sink is called under Processing Run Control's
  lock, and the Game Context sink is UI-thread-affine.
- The defaulted `DatabasePath` compare-and-set stays in the run sink adapter. It handles re-entrancy (#60), which is
  adapter-side.
- `UserWorkflow` still constructs both adapters. Everything else in ADR-0012 and ADR-0013 stands.

**Everyone else writes through the same operation.** The Plugin List Presentation Adapter stays a direct ViewModel writer
(ADR-0009), and `UserWorkflow`'s picker failures and the shell's catches stay where they are. All three append through
`AddMessage`. Moving their wording into renderers is a separate question.

## Considered options

- **A new Core module owns the Message List rules and writes the ViewModel's collections.** Rejected. The collections
  belong to the ViewModel, so an outside owner would need write access to them, and the lists would stay open.
- **One adapter class implementing both sinks.** Rejected. It blurs the two ordering contracts that ADR-0013 kept apart.
- **Keep the adapters nested and keep testing through `UserWorkflow`.** Rejected. It leaves the adapter tests paying for
  admission, a Plugin List and an executor they do not exercise.
- **A source-text architecture test pinning who may call `AddMessage`.** Rejected. Read-only collections make the
  ownership a type-level fact, so a reflection guard checks it, and a grep-based pin would add a brittle test.

## Consequences

- `UserWorkflowTests` keep input capture, disposal, composition, and one wiring test per sink. The adapter tests move to
  `ViewModelGameContextSinkTests` and `ViewModelProcessingRunControlSinkTests`, which call each sink directly against a
  real ViewModel with `SynchronousThreadDispatcher`.
- `MainWindowViewModelTests` lose the three collection-replacement tests, and the cap tests add 11 messages and expect 10.
- `ProjectedProperties_PublicContract_…` also requires the three Message Lists to be read-only collections with no public
  setter.
- AGENTS.md describes each sink as having "a sink adapter that `UserWorkflow` composes", not "a sink that `UserWorkflow`
  implements".
- WinUI keeps its `InternalsVisibleTo` access to Core (ADR-0013).
- This ADR records the decision ahead of the change.
