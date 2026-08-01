# ADR-0005: Entry Extraction reads Mutagen's required name aspect

- Status: Accepted
- Date: 2026-07-31

## Context

Entry Extraction chose an Entry through four tiers: a record's EditorID, a cast to Mutagen's `INamedGetter` aspect, a
cached reflective lookup keyed by the record's runtime type, and the Synthesized Entry of ADR-0004.

The third tier could not return a name. It searched the record's interfaces for one whose name contained the literal
`INamedGetter`, which matches only `INamedGetter` itself, then asked that aspect's `Name` — a plain `string?` — for a
nested `String` property strings do not have, and returned a null extractor. It was also only reached when the second
tier had already read the same `INamedGetter.Name` and found it empty, so even a widened match would have re-read a
property known to hold nothing.

That much was filed as issue #51, which framed the choice as delete the tier or replace it with an
`ITranslatedNamedGetter` cast, and asked which was true before choosing.

Neither, as it turned out. A survey of all 1118 concrete record classes across the Skyrim, Fallout 4, Oblivion and
Starfield assemblies — every non-abstract type assignable to `IMajorRecordGetter`, including the internal binary
overlay classes a Processing Run actually reads — found the real shape of Mutagen's name aspects:

- `ITranslatedNamedGetter` derives from `INamedGetter`, so it cannot reach a record the second tier misses. Its
  generated implementation is literally `string? INamedGetter.Name => this.Name?.String` — the same value the reflective
  tier was trying to reach. As a replacement it would have been pure cost.
- `INamedGetter` is not, however, the widest name aspect. Mutagen models a display name twice: an optional
  `INamedGetter`, whose `Name` is nullable, and a required `INamedRequiredGetter`, whose `Name` always has a value. The
  optional aspect derives from the required one, not the reverse, and **ten record types implement only the required
  one** — Skyrim's `Class`, `Key`, `Flora`, `Eyes` and `CollisionLayer`, Fallout 4's `Key`, `Flora` and
  `CollisionLayer`, and Starfield's `Planet` and `CollisionLayer`.

So the dead tier sat directly on top of a live gap. Every record of those ten types carrying no EditorID was stored
under a Synthesized Entry — `[Class_000800]` — with its actual name sitting unread on the record. The reflective tier
named exactly the case it failed to cover.

## Decision

Entry Extraction casts to **`INamedRequiredGetter`**, and the reflective tier and its process-wide
`ConcurrentDictionary` cache are deleted.

One cast covers every named record type. `INamedGetter`, `ITranslatedNamedGetter` and `ITranslatedNamedRequiredGetter`
all derive from `INamedRequiredGetter`, and those four are the only name aspects Mutagen defines, so nothing reachable
before is lost and the ten required-only types are gained.

The values agree where the aspects overlap. A record implementing the optional aspect generates
`string INamedRequiredGetter.Name => this.Name?.String ?? string.Empty` for a translated name and
`=> this.Name ?? string.Empty` for a plain one — the same string the old cast read, with null collapsed to empty. The
existing emptiness check already routes both to the Synthesized Entry, so no record changes its Entry except the ten
types that were never read at all.

A required name cannot be null, but it can be empty: Mutagen substitutes an empty value for an absent name subrecord
rather than reporting the field missing, and a binary overlay does the same, returning `TranslatedString.Empty` or
`string.Empty` from its own generated member. So a required-named record with no name on disk still falls through to
the Synthesized Entry rather than storing a blank Entry or raising a Processing Warning.

This is preferred over the two directions issue #51 listed:

- **Deleting the tier outright** is right about the tier and would have shipped the gap it was hiding. The survey is
  what turned a dead-code cleanup into a defect fix.
- **Replacing it with an `ITranslatedNamedGetter` cast** reaches nothing a cast to `INamedGetter` did not already
  reach, because the translated aspect derives from the optional one.

`MutagenNameAspectTests` holds both facts the single cast rests on — that every aspect in Mutagen's aspects namespace
exposing a display name derives from `INamedRequiredGetter`, and that record types exist whose name only that aspect
reaches. They assert against a third-party library deliberately: a Mutagen upgrade that invalidates either should fail
by name rather than surface as a handful of records quietly losing their Entries again.

## Trim and AOT obligations

`EntryExtraction` carried two `[RequiresUnreferencedCode]` annotations for the reflective tier, and because that
obligation propagates to every caller, so did ten more methods across `PluginIngestion`, `IPluginIngestion`,
`ProcessingRun`, `UserWorkflow` and the WinUI click handler. All twelve, across six files, described reflection the
application no longer performs, so all twelve are removed. Nothing in the Processing Run path is trim-hostile on this
application's account any more.

`WinUiPlatformServiceSourceTests.WinUiMainWindow_WiresProcessingWorkflow` asserted the annotation's presence on the
WinUI handler; it now asserts its absence, so re-introducing one at the UI layer is a deliberate edit.

## Consequences

- A record of one of the ten required-named types with no EditorID now stores its real name — `Novice`,
  `Skeleton Key` — where it used to store `[Class_000800]`. This is a user-visible improvement to the Entry column and
  the first behaviour the affected record types have ever had beyond the fallback.
- **Existing databases are not migrated**, for the reasons ADR-0004 gives at greater length: the FormID Record Store
  has no migration mechanism, and a user who wants the better Entries re-runs the Plugin under an Update Mode run.
- Entry Extraction is three tiers rather than four, holds no static mutable state, and performs no reflection. The
  per-type delegate cache existed only to amortise the lookup it never completed.
- Records whose name is genuinely absent are unaffected: they still reach the Synthesized Entry, and ADR-0004's
  assertions — which use `Npc`, an optional-named type — still pass unchanged. Fixing this tier moved rows out of the
  fallback label, exactly as ADR-0004 predicted it would, without changing the label itself.
- Oblivion gains nothing. Every Oblivion record type carrying a name carries an optional one, which is why the tests
  covering this run over three families rather than four; that narrowing is itself asserted rather than assumed.
