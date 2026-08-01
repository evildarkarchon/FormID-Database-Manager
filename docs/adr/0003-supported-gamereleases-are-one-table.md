# ADR-0003: Supported GameReleases are one table

- Status: Accepted
- Date: 2026-07-31

## Context

Everything this application knows about a GameRelease was spread across four sites, none of which referenced any
other:

- the list of GameReleases offered in the game dropdown, a hardcoded list in `MainWindowViewModel`
- the FormID Record Store table name, a whitelist switch in `GameReleaseTableNames`
- the Base Game Plugin set, a second switch in `BaseGamePlugins`
- Plugin overlay construction, a third switch inside `MutagenPluginOverlayReader`

Adding a release meant editing all four, and three of them failed silently when missed. Omitting the dropdown entry
meant the release simply never appeared. Omitting the Base Game Plugin set meant `ForRelease` returned an empty set,
so the Plugin List quietly stopped hiding base game Plugins for that release. Omitting the table name or the overlay
branch at least threw, but only once a Processing Run was already under way.

The gap was real and unnoticed. Mutagen defines eleven GameRelease values; this application supported ten.
`OblivionRE` (Oblivion Remastered) was absent from all four sites, but only the table-name whitelist said so — the
Base Game Plugin lookup returned an empty set for it, the dropdown omitted it without comment, and the Plugin List
Source guard admitted it because it checked that the value was *defined* by Mutagen rather than *supported* by this
application, despite its message reading "Unsupported GameRelease value".

Nothing in the test suite compared what Mutagen defines against what this application handles, so a twelfth release
would have arrived with no signal either.

## Decision

One constant table, `SupportedGameReleases`, holds one row per GameRelease this application can process. Each row
carries the table name, the Base Game Plugin set, and Plugin overlay construction for that release. It replaces
`GameReleaseTableNames` and `BaseGamePlugins`, both of which are deleted rather than kept as forwarding shims — a
forwarder would mean the same fact has two names and the complexity would merely have moved.

The interface is three members: `All` (rows in display order), `ForRelease` (the row, throwing for anything not in the
table), and `IsSupported` (a non-throwing membership check). `IsSupported` exists because the Plugin List Source guard
needs a non-throwing answer; it is a narrower answer to that need than a nullable row, and no other caller needs it.

The row type `SupportedGameRelease` is a sealed class rather than a record, because the overlay member is a delegate
and delegates compare by reference, which would give a record surprising value-equality semantics.

`ForRelease` throws `ArgumentOutOfRangeException` for any GameRelease not in the table. One rule covers both
`OblivionRE` and an entirely undefined enum value; there is no third category and no code path where an unsupported
release quietly produces an empty or default answer. This is safe because every GameRelease in flight in production
originates either from the game dropdown — now sourced from `All` — or from Game Installation detection, which never
returns `OblivionRE`.

The Plugin List Source guard changes from `Enum.IsDefined` to `IsSupported`, which makes its existing message true and
moves rejection to the Plugin List Source boundary, where the caller has context, rather than deep inside a lookup.

Rows are written in exactly the dropdown order that preceded this change. That order is part of the table's
documented interface and is pinned by a test. It reads accidental — it splits the two Fallout entries across the list
— and is expected to change later, but as its own change with its own reasoning, not inside a consolidation.

Game Installation detection stays out of the table, per ADR-0002. Detection is an ordered decision tree over the file
system — the Skyrim branch checks for Enderal, then GOG markers, then VR, then falls back — not a per-release
constant. Flattening it into rows would need a per-row priority field to preserve that ordering, which is a worse
interface than the one it would replace.

The `MutagenPluginOverlayReader` adapter survives. It implements the overlay seam and owns normalisation of expected
Plugin-read failures, which is different knowledge from "which Mutagen type does this release use". Only its
ten-branch switch becomes a lookup and a delegate call; no new seam is introduced for the delegate, because it is data
in a constant table reached through the overlay seam that already exists.

A drift test compares every GameRelease Mutagen defines against the table. A release that is neither supported nor
listed as a deliberate exclusion fails the suite by name.

## Relationship to ADR-0002

ADR-0002's clause that `GameReleaseHelper` is renamed to `GameReleaseTableNames` and retains `GetSafeTableName`,
"whose explicit whitelist switch remains the SQL-injection guard for table names", is superseded. The guarantee is
kept in full — table names remain literal strings in a closed set, and an unsupported GameRelease cannot produce one —
but the guard's location moves from a dedicated module to the `TableName` column of this table. ADR-0002 is otherwise
unchanged, including its ruling that Base Plugins are a constant table read directly by `PluginList` rather than an
injected dependency, and its placement of Game Installation resolution.

## Consequences

- Adding a release is one row. Omitting any of its three facts is a compile error rather than a silent gap.
- "Which games does this support?" has a literal answer instead of an implied intersection of four switches.
- Two deliberate behaviour changes, neither reachable in production: the Base Game Plugin lookup throws for an
  unsupported release where it previously returned an empty set, and `OblivionRE` is rejected one layer earlier, at
  the Plugin List Source boundary rather than inside a lookup.
- Existing FormID Record Store databases keep working, because table names are unchanged.
- The tests at the four consuming seams are untouched by this change, which is what makes them regression evidence
  that consolidation changed no behaviour.
- `MutagenPluginOverlayReader` is referenced once in the entire test suite, so overlay construction remains
  effectively untested. This change does not alter that; the drift and completeness tests are its safety net until
  generated per-family Plugin fixtures make real coverage possible.
- Readable game names in the dropdown become a single-field follow-up rather than another scattered lookup.
- Whether to support Oblivion Remastered is now an explicit, loudly enforced decision rather than an accident.
