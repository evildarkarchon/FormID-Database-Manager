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
- Two consequential exception-type changes fall out of the single `ForRelease` contract, both for an unsupported
  release only. `FormIdRecordStore.OpenAsync` — a public member — now throws `ArgumentOutOfRangeException` where it
  threw `ArgumentException`; since the former derives from the latter, existing `catch` sites are unaffected and only
  an exact-type assertion notices. `MutagenPluginOverlayReader.ReadOverlay` now throws `ArgumentOutOfRangeException`
  where it threw `NotSupportedException`. The overlay row is therefore resolved *outside* that adapter's `try`,
  because its expected-failure check treats `ArgumentException` as a malformed-Plugin signal and would otherwise
  normalize a programming error into a Failed Plugin.
- Existing FormID Record Store databases keep working, because table names are unchanged.
- The tests at the consuming seams are preserved as regression evidence wherever the behaviour they pin is
  unchanged — most importantly the six-release Plugin List hiding theory and the Store's schema coverage, both
  untouched. Four seam tests did change, each for a reason recorded above: the Plugin List test that pinned
  `OblivionRE` producing a Plugin List which hid nothing, the Store test that pinned the old exception type, the
  Plugin List Source test that gains a Mutagen-defined-but-unsupported case, and the two ViewModel game-list tests
  that collapse into one projection assertion now that content and order are the table's responsibility.
- `MutagenPluginOverlayReader` was referenced once in the entire test suite when this decision was taken, so overlay
  construction was effectively untested; the drift and completeness tests were its only safety net. That is no longer
  the case, and this bullet is retired in two parts. `MutagenPluginOverlayReaderTests` drives the real adapter against
  real paths and pins both the failure-classification behaviour and — directly relevant here — the placement of the
  `ForRelease` lookup outside the adapter's `try`, so the reclassification hazard described above fails the suite by
  name. `PluginOverlayConstructionTests` then covers the happy path for every row, using a Plugin generated at test
  time for that row's release: it asserts the GameRelease the returned overlay reports and its concrete Mutagen
  family, which together catch both a swap within a game family and a swap across families. A table-driven guard
  fails by name for a row with no fixture, mirroring the drift test above.
- The Plugin fixture generator carries its **own** release-to-Mutagen mapping, deliberately duplicating the column
  this table consolidated. Deriving it from the table would make the wiring assertion circular — a row wired to the
  wrong release would generate a fixture for that same wrong release and agree with itself. The per-family main master
  name each fixture declares is duplicated for the same reason, rather than read from the Base Game Plugin sets.
  This follows the convention the table's own tests already established, which re-list every Base Game Plugin set and
  the full dropdown order rather than deriving them. It is test-side only: "adding a release is one row" still holds
  for production, and the coverage guard is what makes the second list impossible to forget. This is not the drift
  this ADR eliminated; it is a deliberate second opinion, and it runs in the direction that cannot be circular.
- Reading a Plugin the fixtures generate revealed three defects, each pinned by a characterization assertion rather
  than fixed, and each filed: the synthesized Entry label leaks Mutagen's overlay class name so it differs from an
  in-memory read (#50); Entry Extraction's reflection name-lookup tier can never succeed, so every record without an
  EditorID or a display name falls to that label (#51); and a Starfield Processing Run aborts with an unhandled
  master-resolution failure when `Starfield.esm` is absent from the resolved Data directory (#52). Pinning rather
  than fixing follows the same reasoning as the dropdown order above — each changes something user-visible and
  deserves its own change with its own reasoning.
- Readable game names in the dropdown become a single-field follow-up rather than another scattered lookup.
- Whether to support Oblivion Remastered is now an explicit, loudly enforced decision rather than an accident.
