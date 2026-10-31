# ADR-0016: Plugin-read failures are classified by fault scope

- Status: Accepted
- Date: 2026-10-31

## Context

ADR-0015 moved Plugin Ingestion's three Mutagen failure rules, unchanged, behind the Plugin read seam into
`MutagenPluginReader`. That left the question in #127 open: the rules decide by **when** an exception arrives, not by
what it means.

- **Opening** walks the inner-exception chain against six families (`MalformedDataException`, `RecordException`,
  `ArgumentException`, `OverflowException`, `IOException`, `UnauthorizedAccessException`). A match makes a Failed Plugin,
  and anything else fails the run. The Unresolvable Master check comes *after* that list and looks only at the
  top-level exception. So a `MissingModException` with a listed cause in its chain became a Failed Plugin, and a wrapped
  one failed the run without naming the master.
- **Enumeration** treats only a top-level `RecordException` as a Failed Plugin. That looks narrow, but it is the widest
  rule of the three. Mutagen's generated `EnumerateMajorRecords()` ends in
  `.Catch(e => RecordException.EnrichAndThrow(e, ModKey))`, so *every* failure leaving `MoveNext` is a `RecordException`,
  including a `NullReferenceException` from a Mutagen defect.
- **Per record**, Entry Extraction catches everything except cancellation. It drops the record silently when the
  message matches `IgnorableErrorPatterns`, and otherwise reports a Processing Warning. Checked against the referenced
  Mutagen source, that list is close to dead:
  - "KSIZ" and "KWDA" appear only in `SubrecordException.ToString()`, never in `Message`.
  - "Expected EDID", "List with a non zero counter" and "Failed to parse record header" do not exist.
  - "Unexpected record type" is raised during enumeration, where the filter never runs.
  - No plausible `NullReferenceException` exists in the `FormKey`, `EditorID` or `Name` getters.

Facts about the overlay that the decision relies on:

- Mutagen copies each group into memory during `MoveNext`. `FormKey` and `EditorID` never touch the file.
- `Name` touches the disk only on a localized Plugin, when it lazily loads `.STRINGS` files. That load sits in an
  exception-caching `Lazy<>`, so one bad strings file fails `Name` for every record that uses it.
- Getters throw raw exceptions; Mutagen's enrichment wrappers (`RecordException`, `SubrecordException`,
  `ModGroupsMalformedException`) apply only to opening and enumeration.

## Decision

**A Plugin-read failure becomes the outcome for the narrowest thing it is a fact about, whichever phase it arrives in.
Phase only limits which outcomes are reachable.**

| The failure is about | Outcome |
|---|---|
| The Data directory: an Unresolvable Master | The Processing Run fails and names the master (ADR-0006) |
| The Plugin's data, while opening or enumerating | Failed Plugin |
| One record, while reading its FormID or Entry | Record Issue: a Processing Warning, with the record dropped, or stored with a Synthesized Entry when only its Entry failed |
| Anything the application does not recognise | The Processing Run fails, naming the Plugin it was reading |

The rules in `MutagenPluginReader`:

1. **The Unresolvable Master check runs first.** It finds `MissingModException` or `MissingModMappingException`
   anywhere in the inner-exception chain, so a master-resolution failure always fails the run, whatever it wraps or is
   wrapped by.
2. **One Plugin-data family list serves every phase.** It holds the six families above plus three Mutagen raises that
   today become Failed Plugins only because the enumeration wrapper hides them:
   - `InvalidDataException`, from a short group read;
   - `IndexOutOfRangeException`, from truncated subrecord slices;
   - `RecordCollisionException`, from duplicate FormKeys within the Plugin.
3. **A failure is classified by its cause, behind Mutagen's enrichment wrappers.** `RecordException`,
   `SubrecordException` and `ModGroupsMalformedException` are looked through rather than matched.
   - A listed cause makes a Failed Plugin.
   - A bare `RecordException` that Mutagen raised itself, with no inner cause, makes a Failed Plugin.
   - An unlisted cause fails the run.
4. **A per-record failure is never fatal to the Plugin.** A listed cause in a getter is a Record Issue, including an
   `IOException` from a `.STRINGS` load: that costs the record its Entry, not its FormID. An unlisted cause in a getter
   fails the run.
5. **`IgnorableErrorPatterns` is deleted.** No record is dropped without being counted in its Plugin's Processing
   Warning, and no classification matches message text.
6. **The diagnostic message is the classified cause's**, in every phase: the exception that matched the list, or the
   bare `RecordException`. Mutagen's wrappers copy the inner message, so what users read barely changes.
7. **A run failure from an unlisted cause carries the Plugin and phase.** It is a typed exception wrapping the original,
   on the model of `UnresolvableMasterException`, and `ProcessingRunPresentation` renders it with the Plugin's name. It
   still leaves the run as an exception, so ADR-0007 holds.

Cancellation is unchanged. `NestedCancellation.RethrowIfPresent` runs before any classification.

`DescribeRecordType`'s catch-all around the Loqui registration stays outside the classification. Its fallback to the
runtime type name is reachable only by a record that breaks Mutagen's contract, such as a test double, and dropping or
warning there would cost FormID coverage over a missing third label (ADR-0004).

## Considered options

- **Keep three phase-based rules and record why each is shaped as it is.** Rejected. The cases in #127 show the same
  fault ending three ways, and no reason exists for an `IOException` to mean one thing at opening and another during
  enumeration.
- **Align opening and enumeration, and leave per-record extraction as a catch-all.** Rejected. A catch-all there turns
  an application defect into a warning, which is what #49 rejected for opening.
- **Keep classifying enumeration by the top-level `RecordException`.** Rejected. Because Mutagen wraps everything, it
  makes every defect inside Mutagen a Failed Plugin, and disguises it as a broken Plugin. The cost of the chosen rule is
  that such a defect now fails the run; rule 7 names the Plugin so the user can deselect it.
- **Make a per-record `IOException` a Failed Plugin**, as "about the Plugin's file" would suggest. Rejected. It can only
  come from a strings load, which loses names, not FormIDs; failing the Plugin would throw away every good FormID.
- **Keep `IgnorableErrorPatterns`.** Rejected. It matches message text that this Mutagen version almost never produces,
  so it silently drops nothing today and could silently drop anything after an upgrade.
- **Let an unlisted cause propagate raw, as it does today.** Rejected. The generic failure does not say which Plugin was
  being read, which is the one fact the user needs to work around it.

## Consequences

- Enumeration gets narrower than today. A Mutagen defect triggered by one Plugin fails the run rather than that Plugin.
- A master-resolution failure wrapped around a listed cause is now an Unresolvable Master, not a Failed Plugin.
- Records the ignore list would have dropped, if any ever matched, are counted in a Processing Warning.
- `MutagenPluginReaderTests` and `EntryExtractionTests` gain cases per family and per phase: a wrapped listed cause, a
  bare `RecordException`, an unlisted cause in each phase, a chain-wrapped master failure, and a `.STRINGS` `IOException`
  from `Name`. `ProcessingRunPresentationTests` gain the wording for rule 7.
- ADR-0006 and ADR-0015 are amended by dated sections pointing here.
- This ADR records the decision ahead of the change (#127).
