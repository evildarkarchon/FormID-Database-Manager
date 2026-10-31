# ADR-0015: The Plugin read seam hides Mutagen

- Status: Accepted
- Date: 2026-10-30

## Context

ADR-0008 left one seam between Plugin Ingestion and Mutagen: `IPluginOverlayReader`, which only *opens* a Plugin. It
hands back Mutagen's `IModDisposeGetter`. Plugin Ingestion then enumerates `IMajorRecordGetter`s itself and passes each
one to Entry Extraction. An architecture review on 2026-10-30 found what that leaves on the wrong side of the seam:

- **Failures are classified in three modules.** The overlay reader turns six exception families into
  `PluginOverlayReadException`. Plugin Ingestion turns that into a Failed Plugin, turns a `RecordException` during
  enumeration into a Failed Plugin too, and turns `MissingModException` and `MissingModMappingException` into an
  Unresolvable Master (ADR-0006). Entry Extraction catches everything per record and turns it into a Processing Warning,
  or drops the record when `IgnorableErrorPatterns` matches its message. Whether a given Mutagen exception ends as a
  Failed Plugin, a Processing Warning, or a failed Processing Run can only be answered by reading all three files.
- **Cancellation is unwrapped three times.** Each of the three modules has its own copy of `RethrowNestedCancellation`.
- **Plugin Ingestion's tests speak Mutagen.** About a dozen fakes in `PluginIngestionTests` and
  `PluginIngestionPlanTests` build Moq'd `IModDisposeGetter`s and `IMajorRecordGetter`s just to say "this Plugin has
  these FormIDs" or "this Plugin fails to read".

## Decision

**Mutagen sits entirely behind one Plugin read seam. What crosses it is FormIDs, Entries, and classified failures.**

- `IPluginReader` replaces `IPluginOverlayReader`. `Open(SelectedPluginReady)` returns an `IOpenedPlugin`, and
  `IOpenedPlugin.ReadRecords(Action<string> reportRecordIssue, CancellationToken)` lazily yields `FormIdRecord`s. Plugin
  Ingestion still disposes the opened Plugin, so ADR-0008's "Plugin Ingestion still owns overlay lifetime" stands.
- `MutagenPluginReader` is the production adapter, and it alone classifies:
  - opening failures, using the current expected-failure list;
  - `RecordException` during enumeration;
  - per-record failures, through Entry Extraction and `IgnorableErrorPatterns`;
  - `MissingModException` and `MissingModMappingException`, which it throws as `UnresolvableMasterException`. That is a
    catch of its own beside the Failed Plugin list, not an addition to it, so the list does not widen and ADR-0006's
    outcome is unchanged.

  It keeps the only copy of `RethrowNestedCancellation`, shared with Entry Extraction.
- One internal `PluginReadException`, carrying `PluginReadPhase` and the diagnostic message, reports a Failed Plugin
  from either phase. It replaces both `PluginOverlayReadException` and Plugin Ingestion's private nested exception of
  the same name. It has to stay an exception during enumeration: the Store consumes the record stream inside its atomic
  Plugin write, and an exception thrown through that write is what rolls the Plugin back.
- Entry Extraction stays its own internal class, constructed by `MutagenPluginReader`. In the Glossary it is still part
  of Plugin Ingestion; only its code moves to the Mutagen side of the seam. ADR-0004 and ADR-0005 carry over unchanged.
- Recoverable record issues leave the seam through the `reportRecordIssue` callback, after the ignore-list filter has
  run. Plugin Ingestion keeps `RecordWarningCollector` and builds the Processing Warning, because a Processing Warning
  is a Processing Run concept.
- Plugin Ingestion keeps:
  - selection order and cancellation between Plugins;
  - skips and Update Mode;
  - the Store write;
  - disposal of the opened Plugin, with its first-failure-wins rule;
  - turning `PluginReadException` into a Failed Plugin or a planned failure.

  Its constructor becomes `(IGameLoadOrders, IPluginReader)`. `PluginIngestion.cs` and `PluginIngestionContracts.cs`
  reference no Mutagen type.
- **Behavior does not change.** The three classification rules keep their current, differing scopes after they move.
  Whether they should become one rule is #127.

## Considered options

- **Keep the master-resolution classification in Plugin Ingestion, as ADR-0006 point 1 placed it.** Rejected. It keeps
  Mutagen exception types in Plugin Ingestion. What ADR-0006 needed was that the Failed Plugin list not widen, and a
  separate catch in the adapter keeps that.
- **Keep Entry Extraction in front of the seam and hand Plugin Ingestion a record view.** Rejected. Every tier of
  Entry Extraction reads Mutagen: `EditorID`, `INamedRequiredGetter` (ADR-0005), and the Loqui registration
  (ADR-0004). No Mutagen-free view of a record exists for it to work from.
- **Let the seam own the opened Plugin's lifetime**, for example by opening inside the record iterator. Rejected. A Dry
  Run needs to open a Plugin without reading it. Opening failures would surface inside the Store write instead of
  before it. And the rule that keeps the first failure's identity through disposal belongs with the `try` it guards.
- **Have the opened Plugin collect a `ProcessingWarning` for Plugin Ingestion to read after enumeration.** Rejected. It
  adds a rule about when the warning may be read (only once the Store has drained the stream), and moves a Processing
  Run concept behind a Mutagen adapter.
- **A result type for opening and an exception for enumeration.** Rejected. That gives two ways of reporting one
  outcome.
- **Merge the three classification rules in the same change.** Rejected. It changes what users see and deserves its own
  reasoning (#127).
- **Test the adapter's classification only with real broken Plugin fixtures.** Rejected. A fixture cannot produce a
  `RecordException` midway through enumeration on demand.

## Consequences

- `MutagenPluginReader` has one internal test constructor taking an overlay-opening delegate, which defaults to the
  Supported GameRelease's `CreateOverlay`. That one delegate is the adapter's whole test seam, unlike the four-delegate
  constructor ADR-0008 removed.
- The Mutagen-fed scenarios in `PluginIngestionTests` and `PluginIngestionPlanTests` move, with the same assertions, to
  `MutagenPluginReaderTests`: opening and record-reading failures, unexpected failures, both master-resolution failures,
  and nested cancellation. `PluginOverlayConstructionTests` and `PluginIngestionFixtureTests` stay as the end-to-end
  check against real Plugins. The `TryExtract` tests test Entry Extraction directly.
- Plugin Ingestion's fakes yield `FormIdRecord`s and throw `PluginReadException` or `UnresolvableMasterException`.
- A new architecture test pins that `PluginIngestion.cs` and `PluginIngestionContracts.cs` contain no Mutagen
  reference. The pins on `IPluginOverlayReader.ReadOverlay` in `GameLoadOrdersArchitectureTests` and
  `CoreProjectBoundaryTests` are renamed. AGENTS.md gains an enforced-boundary line for the seam.
- ADR-0006 point 1 and ADR-0008's "alone classifies" sentence are amended by dated sections pointing here.
- This ADR records the decision ahead of the change.
