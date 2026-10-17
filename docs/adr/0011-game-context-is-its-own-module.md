# ADR-0011: Game Context is its own module, and it builds the Plugin List Source

- Status: Accepted
- Date: 2026-10-17

## Context

ADR-0002 kept Game Context as "the User Workflow's selection state" inside `UserWorkflow`. Back then it was one private
record. It has since grown into a module in all but name:

- a latest-intent generation counter that retires install-location lookups, detection, and Browse results once the
  user has moved on;
- install-location lookup and detection, moved off the UI thread;
- Browse, where opening the picker is itself an intent;
- driving the Plugin List, which every transition ends by invalidating or refreshing, gated on the generation;
- its own ViewModel projection and four user-facing messages.

That is about 290 of the 970 lines in `UserWorkflow`. The run code shares exactly one read with it: Processing Run
Admission captures the selected GameRelease and game directory.

Game Context also held the game directory as a raw string, and three later steps validated it, each with different
wording. Detection reported "Could not read the selected directory". `PluginList.RefreshAsync` threw from Browse into
the shell's "Unexpected error". Admission threw, and the user saw "Error processing FormIDs". "Is this the same
directory?" was answered with a raw `OrdinalIgnoreCase` comparison in Game Context, and with canonical
`PluginListSource` equality everywhere else. Tests that wanted a confirmed Plugin List next to an empty Game Context had
to refresh the Plugin List behind the workflow's back.

## Decision

**A `GameContext` module owns Game Context: its state, its transitions, and the Plugin List Source built from it.**

- **What it owns.** `GameContext` holds the selected GameRelease, the displayed game directory, the Plugin List Source,
  the available directories, and Advanced Mode, exposed as an immutable `GameContextState` through `Current`. It owns:
  - the four transitions: select GameRelease, select detected directory, set Advanced Mode, and Browse, including the
    picker call;
  - the generation counter;
  - placing install-location lookup and detection off the UI thread;
  - invalidating and refreshing the Plugin List;
  - its own ViewModel projection and messages.
- **The source is built when a directory is adopted.** A directory is held only together with a GameRelease, and only
  when the two make a Plugin List Source.
  - **Refused picks.** A directory that cannot become a source is refused when it is picked, and so is a Browse where no
    game is detected. A refused pick leaves the Game Context, and the Plugin List, exactly as they were.
  - **Detected directories.** Install-record directories are checked when they are listed, so a pick from the detected
    list is never refused.
  - **Change detection.** Whether a pick changes anything is decided by source equality.
- **Sources, not strings, cross the seam.**
  - `PluginList.RefreshAsync` takes a `PluginListSource`.
  - Processing Run Admission takes the GameRelease and the source. It no longer canonicalizes, and it no longer throws
    for a malformed directory.
  - `GameContext` is the only production caller of `PluginListSource.Create`.
- **Ownership.**
  - `UserWorkflow` constructs `GameContext` from `GameInstallations`, `IFileDialogService`, the Plugin List, and the
    ViewModel. It forwards Game Context intents to it and retires it in the same disposal slot the generation increment
    held.
  - `GameContext` borrows the Plugin List. `UserWorkflow` still owns the Plugin List's lifetime, because it still reads
    the Plugin List for admission and selection.
  - `UserWorkflow.Create` stays the only production composition.
- **Threading.** `GameContext` is UI-thread-affine by contract. It is entered from UI handlers, and its continuations
  return to the UI context. The generation counter stays interlocked, because lookups finish on the thread pool.

This supersedes ADR-0002's clause that Game Context "stays inside `UserWorkflow`". The rest of ADR-0002 stands: Game
Installation resolution, canonicalization, and the probe seam are unchanged.

## Considered options

- **Keep Game Context in `UserWorkflow`, and only hold a validated source.** Rejected: it removes the three wordings,
  but leaves two unrelated concurrency mechanisms in one 970-line module. They are the Game Context generation counter
  and the run gate, and they share nothing except one read.
- **A state-only Game Context that returns "refresh with source X" or "invalidate" for `UserWorkflow` to carry out.**
  Rejected: the refresh is only valid while its generation is still the latest. That check would have to travel with
  the instruction, which leaks an ordering rule to every caller.
- **Publish typed state and notices, and project them through a separate adapter, as the Plugin List does.** Rejected
  for now: there would be one adapter, so the seam would be hypothetical. The Plugin List's adapter earns its separation
  through cross-thread publishing and revisions, and Game Context has neither.
- **Keep the picker in `UserWorkflow`, and pass the chosen path to Game Context.** Rejected: opening the picker has to
  retire older lookups before the dialog returns. That needs a "begin Browse" token, which is another ordering rule
  crossing the seam.
- **Keep an unusable directory and refuse the run instead,** with a new `RunRejectionReason`. Rejected: that renames the
  error mode instead of removing it, and nothing downstream should have to defend against an unusable directory.

## Consequences

- One message for an unusable directory. `RunPress.AdmissionFailed` and the workflow's `catch (ArgumentException)`
  around admission are deleted, and admission's `ArgumentException` mode goes with them.
- `PluginList.RefreshAsync` no longer validates a directory, so it loses its `ArgumentException` mode.
- Game Context tests move from `UserWorkflowTests` to `GameContextTests`. They drive the module directly over a real
  Plugin List, an in-memory installation probe, and a faked picker. `UserWorkflowTests` keep forwarding, disposal order,
  and one end-to-end test that picks, loads, and admits.
- Architecture tests pin three things:
  - `GameContext.cs` is the only production caller of `PluginListSource.Create`.
  - `UserWorkflow.cs` does not reference `GameInstallations`.
  - `GameContext.cs` references no Processing Run types.
- Picking `C:\Game` and then `C:\Game\Data` no longer refreshes the Plugin List, because both make the same source.
- `UserWorkflow`'s file-picker helper is shared with `GameContext`: the "selected a non-empty path" check moves onto
  `FileDialogResult`, and each caller keeps its own failure wording.
- This ADR records the decision ahead of the change.
