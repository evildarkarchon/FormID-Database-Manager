# ADR-0006: An unresolvable master fails the Processing Run, not one Plugin

- Status: Accepted
- Date: 2026-07-31

## Context

Starfield is the one Supported GameRelease whose load order separates master files by type. Mutagen therefore cannot
read a Starfield Plugin that declares masters unless it is given a master-flags lookup that resolves every declared
ModKey to a `MasterStyle` — and every spec-correct Plugin of every game declares its game's main master first, because
that is what the Plugin format requires.

`GameLoadOrderProvider.BuildSnapshot` built that lookup from the load-order listings whose file exists on disk:

```csharp
if (!_fileExists(pluginPath)) continue;
```

and `GameLoadOrderSnapshot.CreateReadParameters` fell back to `BinaryReadParameters.Default` when the resulting
collection was empty. So a Starfield Data directory holding mods but not `Starfield.esm` — a partial install, or a path
that holds mods only — produced a lookup that could not resolve the master every selected Plugin declares. Mutagen
raised `MissingModException`, or `MissingModMappingException` when no lookup was supplied at all.

Neither type is on `MutagenPluginOverlayReader`'s expected-failure list, and that narrowness is deliberate (issue #49):
an unexpected internal failure should abort loudly rather than be disguised as a bad Plugin. So the exception escaped
normalization and took the whole Processing Run down as an unhandled failure whose message — "Master flag lookup was
not provided." — told the user nothing they could act on.

Issue #52 recorded the defect and left the fix open between three directions.

## Decision

**An unresolvable master fails the Processing Run, with a message naming the master, and does not become a Failed
Plugin.**

Three changes carry it.

1. `PluginIngestion.TryCreateOverlay` catches `MissingModException` and `MissingModMappingException` and rethrows them
   as `UnresolvableMasterException`, carrying the selected Plugin name and — where Mutagen names one — the master file
   name. The classification lives in Plugin Ingestion rather than in the overlay adapter, so the adapter's
   expected-failure list does not widen.

   `MissingModException` can in general carry several ModKeys, and only the first is reported. That is not a narrowing
   here: `SeparatedMasterPackage.Separate` raises it inside the loop over declared masters, from a single ModKey, so
   the one it carries on this path is the one that failed. `MissingModMappingException` names none, because Mutagen
   raises it before looking at any individual master.
2. `GameLoadOrderSnapshot` now distinguishes a null master-style collection from an empty one. Null means the
   GameRelease does not separate master load orders, so no lookup applies and `BinaryReadParameters.Default` is right.
   Empty means it does and the Data directory supplied nothing — and passing the empty lookup through is what makes
   Mutagen raise the `MissingModException` that *names* the master, instead of the `MissingModMappingException` that
   only reports that no lookup existed.
3. `UserWorkflow` shows the failure's message unwrapped, as it already does for `ProcessingRunValidationException`,
   rather than behind the generic "Error processing FormIDs" prefix.

The run stops at the first Plugin that declares an unresolvable master rather than before any Plugin is attempted. That
is precise: only a Plugin that actually declares an unresolvable master triggers it, which the alternatives below are
not.

## Why not the alternatives

- **Add the two failures to the adapter's classification list, making them one Failed Plugin.** Cheapest, and wrong on
  both counts it touches. It widens a list whose narrowness is load-bearing, and it reports a Data-directory problem as
  a Plugin problem — pointing the user at a Plugin that is not broken, once per selected Plugin, since every one of them
  would fail identically.

- **Check the whole load order up front and fail if any listing's file is absent.** Truly "before any Plugin", and
  over-aggressive: a `plugins.txt` entry for an uninstalled mod that no selected Plugin declares as a master is
  harmless, and this would kill an otherwise fine run over it.

- **Read each selected Plugin's declared masters up front and verify them against the lookup.** Precise *and* strictly
  before any Plugin, at the cost of a new header-reading seam and one extra header read per selected Plugin. It buys
  only the ordering guarantee, which matters solely for a Plugin declaring a missing *mod* master after earlier Plugins
  have already been written — a case where stopping at that Plugin is still correct behaviour.

- **Build the lookup from declared masters rather than from files on disk**, guessing a `MasterStyle` for a master that
  is not present. This was the direction issue #52 asked to investigate before settling, because it would make the run
  succeed rather than fail. It was investigated and rejected.

  `SeparatedMasterPackage.Separate` uses each master's style to place it in the Full, Medium or Small bucket and assign
  its index within that bucket. `GetFormKey` then derives the record's `id` from the FormID's own marker byte and picks
  the ModKey out of the bucket the style chose — so the `FormKey.ID` this application stores is *invariant* to what the
  lookup contains, and a wrong guess would not corrupt any persisted value today.

  That is not a good enough reason to guess. The guess is unverifiable for anything other than a game's main master — a
  missing ESL-flagged mod master guessed as `Full` is simply wrong — and its safety rests entirely on an internal
  Mutagen invariant this application neither owns nor tests, which a Mutagen upgrade could change without notice. It
  also makes an unusable Data directory silently produce a database, which hides the problem rather than reporting it.

## Consequences

- A Starfield run against a Data directory missing a declared master now ends as a failed Processing Run whose message
  names the master, instead of an unhandled Mutagen abort. Every other game family is unaffected: only a GameRelease
  with separated master load orders reaches this path.
- `MissingModMappingException` is no longer reachable through the Processing Run path. `GameLoadOrderProvider` is
  unchanged and always passed its collected list — possibly empty — for a GameRelease that separates master load
  orders; what changed is that `GameLoadOrderSnapshot` stopped discarding an empty one. Since `PluginIngestion` is the
  only caller that asks for the lookup (`includeMasterFlagsLookup: true`), every Processing Run now reaches the named
  failure instead. It is still handled, and still covered, because `IGameLoadOrderProvider` is a seam another
  implementation can sit behind.
- `MutagenPluginOverlayReader` is unchanged. `PluginOverlayConstructionTests`' two negative-boundary tests still assert
  that both Mutagen failures escape the adapter unwrapped — which is now what Plugin Ingestion depends on, rather than
  merely a pin on an unfixed defect.
- The run stops rather than reporting per-Plugin outcomes, so no `PluginIngestionReport` is produced and Store
  optimization does not run. Plugins ingested before the failing one keep their rows; the Processing Run is not
  transactional across the selection, and never was.
