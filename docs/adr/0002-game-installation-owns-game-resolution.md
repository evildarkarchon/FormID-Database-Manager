# ADR-0002: Game Installation owns game resolution

- Status: Accepted
- Date: 2026-07-31

## Context

Resolving a Game Installation — which game is installed at a directory, where its installs are, and which Data
directory a selected directory means — was spread across six homes: `GameDetectionService`, `GameLocationService`,
`GameReleaseHelper.ResolveDataPath`, `PluginListSource`, `GameLoadOrderProvider`, and a `GameContextSnapshot` record
private to `UserWorkflow`. `GameDetectionService` and `GameLocationService` are exact inverses of each other, in
separate files, reached through different substitution styles.

That split had three concrete costs.

`ResolveDataPath` computed a normalized path and returned the un-normalized input, so a trailing separator survived.
Its three callers each repaired this differently: `PluginListSource` re-normalized, `GameDetectionService` compared raw
strings and silently fell through to a directory-existence branch, and `PluginIngestion` did not compensate at all. One
rule with three repairs is a defect the split made hard to see.

The cluster reached the file system through static `File.Exists` and `Directory.Exists` calls, so roughly 800 lines of
temp-directory fixtures across `GameDetectionServiceTests` and `GameDetectionIntegrationTests` covered a 184-line
module, with the two files asserting the same detection outcomes for the same releases.

Neighbouring modules were injected three different ways for the same kind of dependency: `IGameLocationService` as an
interface with one adapter, `GameDetectionService` as a concrete class with `virtual` methods so Moq could subclass it,
and `GameLoadOrderProvider` as an interface plus an internal four-delegate constructor bundle. `PluginList` took the
whole `GameDetectionService` as a dependency to call one method on it, and that method returned a private mutable
`HashSet` by reference.

`CONTEXT.md` named Game Context but had no term for the thing on disk, which is why one concept could occupy six homes
without the gap being obvious.

## Decision

A `GameInstallations` module owns Game Installation resolution: Data-directory canonicalization, detection of a
GameRelease from a directory, and location of install directories for a GameRelease. `CONTEXT.md` defines
**Game Installation** as the resolved on-disk facts, distinct from **Game Context**, which remains the User Workflow's
selection state and stays inside `UserWorkflow`.

Canonicalization is a pure static member. It is the single implementation of the rule, called by `PluginListSource`,
`PluginIngestion`, and detection. Production must not introduce a second data-path helper.

`IGameInstallationProbe` — file existence, directory existence, and install-folder lookup — is the module's only
substitution seam, and has exactly two adapters: a production adapter over the real file system and Mutagen's
`GameLocations`, and an in-memory adapter used by tests. The module itself is a concrete sealed class with no
interface, because nothing varies across it; tests obtain their control from the probe. Production must not reintroduce
a detection service, an install-location interface, or `virtual` members for subclass-based faking.

Base Plugins are a constant table, not an injected dependency, and are exposed as immutable sets. `PluginList` reads
that table directly and does not depend on the resolution module.

Game Load Order stays outside this module. It reads Plugin files, is expensive when preparing separated-master reads,
and sits on the ingestion hot path; folding it in would widen the interface where this decision narrows it. ADR-0008
supersedes this ADR's original `IGameLoadOrderProvider.BuildSnapshot` interface shape. Game Load Orders still takes a
canonical Data directory rather than a raw path.

Resolution returns no value type. After base Plugins became a constant table and `PluginListSource` kept the canonical
Data directory, nothing remained for such a type to carry; `Detect` returns a nullable GameRelease, which is what
callers consume. A game root is an implementation detail of detection.

Detection returns null for exactly one reason: no known game master file was found. Malformed input throws rather than
being swallowed. The production probe adapter keeps a catch-all around Mutagen's install-record lookup, because that
API genuinely throws on missing or malformed registry state; that swallow is adapter behaviour, not module behaviour.

The module is synchronous. Callers decide thread placement, and `UserWorkflow` offloads both detection and location.

`GameReleaseHelper` is renamed to `GameReleaseTableNames` and retains only `GetSafeTableName`, whose explicit
whitelist switch remains the SQL-injection guard for table names.

## Consequences

- Data-path canonicalization has one owner, closing the trailing-separator defect at all three call sites.
- Detection rules are tested in memory against the probe; real-file-system tests are reduced to adapter behaviour such
  as read-only and non-existent directories.
- One substitution style replaces three, and `PluginList` no longer depends on detection.
- Two previously unreachable `catch` blocks in `UserWorkflow` become live, and malformed directory input surfaces as an
  error rather than as "could not detect game".
- Detection moves off the UI thread, which it was not before.
- Architecture tests that pin `ResolveDataPath` to a source file by text must be updated, since the member moves.
- `MockFactory`'s remaining live stubs become dead, and Plugin List filtering tests exercise the real base Plugin set
  rather than a three-name stub.
- Carrying canonical paths in `SelectedPluginIngestionRequest` is deliberately left for a separate decision.
  `GameLoadOrderProvider`'s duplicate abstraction — an interface alongside an internal delegate bundle — is resolved
  by ADR-0008.
- This ADR records the decision ahead of the change. The code does not yet have this shape; the migration is sequenced
  so that canonicalization and the defect fix land before detection and location move.
