# ADR-0004: Synthesized Entries name the Mutagen record type

- Status: Accepted
- Date: 2026-07-31

## Context

Entry Extraction stores an EditorID when a record has one, then a display name, then a synthesized fallback label
built from the record's runtime type name and its FormID.

That last tier read the type name off the CLR object it was handed, and which object that is depends on how Mutagen
was asked to open the file. A binary overlay — how every production Processing Run reads records — is an instance of
Mutagen's overlay class, so the stored Entry was `[NpcBinaryOverlay_000802]`. The same record read in memory produced
`[Npc_000802]`.

The Entry is a user-visible column of the FormID Record Store. Users were therefore reading a label that named a
Mutagen internal rather than the record type they would recognise, and reading a *different* label than the one the
project's own tests showed, since every test before the generated Plugin fixtures delivered in-memory records through
a substituted overlay. The label was also hostage to Mutagen's class naming: a release that renamed its overlay
classes would silently change what users see.

The divergence reproduced identically for all four game families and was filed as issue #50, pinned by a
characterization assertion rather than fixed, on the grounds that changing what users see in their databases deserves
its own change with its own reasoning.

## Decision

The synthesized Entry names the record type as **Mutagen's Loqui registration** reports it: `[Npc_000802]`, for an
overlay read and an in-memory read alike.

Mutagen's generated record classes and their overlay counterparts route to the same registration instance —
`Npc_Registration.Instance` is what both `Npc` and `NpcBinaryOverlay` return for `ILoquiObject.Registration` — so its
`Name` is the one label the two reads already agree on. Nothing needs reconciling; the label simply stops being read
off whichever CLR object happened to be constructed.

This is preferred over the two other directions issue #50 listed:

- **Stripping a `BinaryOverlay` suffix from the CLR type name** produces the same string today, but keeps the label
  derived from Mutagen's class naming convention. It fixes the symptom, restates the coupling, and adds a rule that
  is wrong the moment Mutagen names an overlay class anything else.
- **Dropping the type name and using the FormID alone** removes information a user has no other way to recover from
  the Store, since the Store holds no record-type column. The type name is the only thing distinguishing two
  otherwise-anonymous rows.

`IMajorRecordGetter` inherits `ILoquiObject`, so the registration is not an optional capability to test for — a
conforming record always has one, and the lookup is a plain property read rather than a type check. The runtime type
name survives only as a last resort for a record that breaks that contract: a null registration, an empty name, or a
getter that throws. No record Mutagen generates does any of these, which leaves only test doubles on that path. It is
kept because a record reaching this tier has already lost its EditorID and its name, and dropping its row over a
third missing value would cost the user FormID coverage for no gain.

A record that throws while describing its own registration degrades to that fallback rather than raising a Processing
Warning, because it has already been labelled by fallback twice over and a third diagnostic would report the same
fact again. Cancellation is exempt and still propagates, as everywhere else in the module.

The label's shape — `[Type_FORMID]` — is unchanged, and both sites that produced it now go through one helper rather
than formatting it independently.

## Relationship to ADR-0003

ADR-0003 records that reading real overlay records for the first time revealed three defects, "each pinned by a
characterization assertion rather than fixed", and that each deserved its own change with its own reasoning. This is
that change for the first of them, and ADR-0003's final consequence bullet is amended to say so rather than left
reading as though all three are still pinned. #51 and #52 remain pinned as it describes; ADR-0003 is otherwise
unchanged.

## Consequences

- The Entry a user gets for an anonymous record no longer depends on how the Plugin was opened, and names `Npc`
  rather than `NpcBinaryOverlay`.
- A Mutagen release that renames overlay classes can no longer change stored Entries. A release that renames the
  *record* class still can, but that is the same condition under which the label users recognise changes anyway.
- **Existing databases are not migrated.** Rows already holding `[NpcBinaryOverlay_000802]` keep that value until
  their Plugin is ingested again; an Update Mode run replaces a Plugin's rows and picks up the new label. No schema
  migration mechanism exists in the FormID Record Store — its schema is a `CREATE TABLE IF NOT EXISTS` — and
  introducing one to rewrite a fallback label applied to records that have no EditorID and no name is a larger
  commitment than the defect warrants. A user who wants the new labels re-runs the Plugin.
- Only records that reach the fallback tier are affected. An Entry that came from an EditorID or a display name is
  untouched, which is the overwhelming majority of rows.
- Issue #51 is unaffected by this decision, and has since been resolved by ADR-0005: the reflective name-lookup tier
  between the display-name cast and this fallback could never succeed, and was hiding a real gap — ten record types
  whose display name only Mutagen's *required* name aspect reaches. Fixing it moved those rows *out* of this label
  rather than changing the label, as anticipated. It did not fail this ADR's assertions: they use `Npc`, whose name
  aspect is the optional one, so an `Npc` with no name still reaches the fallback and still reads `[Npc_000802]`.
- The characterization assertion #49 left behind is now an assertion of intended behaviour. Its in-memory counterpart
  in `PluginIngestionTests` asserts the same literal, so the two together pin parity rather than either half alone.
