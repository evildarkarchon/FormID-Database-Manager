# ADR-0008: Game Load Orders owns discovery and Plugin-read preparation

- Status: Accepted
- Date: 2026-08-19

## Context

ADR-0002 retained Game Load Order behind `IGameLoadOrderProvider.BuildSnapshot` while deliberately deferring its
duplicate abstraction: callers chose a Boolean master-flags mode and received one snapshot that mixed ordered Plugin
listings, membership, Mutagen master styles, and binary read parameters, while `GameLoadOrderProvider` exposed a
second, nearly implementation-wide test seam through four delegates. That shape made Plugin List discovery and Plugin
Ingestion coordinate Mutagen, filesystem, and separated-master knowledge instead of gaining leverage from the module.

## Decision

A `GameLoadOrders` module exposes one external `IGameLoadOrders` seam with two role-oriented operations that retain the
application's existing `GameRelease` input. `DiscoverAvailablePluginsAsync` returns ordered available-Plugin facts and
owns synchronous-work offloading, bounded raw progress, cooperative work cancellation, and filesystem availability
scanning. It turns only `IOException`, `UnauthorizedAccessException`, and `SecurityException` from external discovery
work into a typed failure fact; cancellation, progress-observer exceptions, programming errors, and fatal failures
propagate. `PluginList` consumes that operation directly; `PluginListDiscovery` and `IPluginListDiscovery` do not remain
as a forwarding seam.

`PrepareSelectedPlugins` accepts one GameRelease, its canonical Data directory, and the ordered selection. It reads the
Game Load Order once, eagerly prepares shared Plugin-read state once, and returns exactly one typed case per selected
Plugin in selection order: not listed, file unavailable with its resolved path, or ready. A ready case carries an opaque
application-owned read capability consumed by `IPluginOverlayReader`; no master-style or binary-read type crosses the
external seam. Preparation opens no overlay, enumerates no record, and touches no FormID Record Store.

A GameRelease without separated master load orders supplies no master-flags lookup. A GameRelease with separated master
load orders always supplies one, including an empty lookup when none of its listed Plugin files is available. Preparation
reads styles from every available listed Plugin, never guesses a missing style, and does not fail merely because a listing
is unavailable. An Unresolvable Master still arises only when overlay opening reaches the first selected Plugin that
declares one, not during eager preparation.

`GameLoadOrderSnapshot`, the Boolean mode, and the four-delegate constructor are removed rather than wrapped. The
implementation retains only ordered names and case-insensitive membership as its Game Load Order fact. One cohesive
`IGameLoadOrderEnvironment` lower seam supplies application-level Mutagen and filesystem observations through a
production adapter and an in-memory test adapter. Tests replace the old seams: module behavior is exercised through
`IGameLoadOrders` with the in-memory environment, while focused production-adapter tests cover Mutagen translation.

This decision supersedes only ADR-0002's original `BuildSnapshot` shape and its explicitly deferred duplicate
abstraction. Game Installation remains separate and `GameInstallations.CanonicalizeDataDirectory()` remains the sole
production implementation of the game-root-or-Data rule. Plugin Ingestion still owns overlay lifetime and alone
classifies Mutagen master-resolution failures as an Unresolvable Master under ADR-0006; a selected-Plugin Dry Run uses
the same preparation and overlay path as a real run.

The migration is intentionally staged. Its first implementation slice adds the complete new module beside the live
legacy path without changing Plugin List, Plugin Ingestion, Dry Run, or overlay callers. Later slices cut those callers
over and remove the provider, snapshot, Boolean mode, and forwarding discovery seam. Interim coexistence is therefore
a checked migration state, not a compatibility adapter or the final architecture described by this decision.

## Considered options

- A minimal listing/read-preparation interface was rejected because it left membership and file-availability
  coordination in both callers.
- A flexible prepared session and a behaviorful Game Load Order value were rejected because lazy caching, lifetime,
  and concurrency semantics enlarged the interface.
- Opening overlays inside Game Load Orders was rejected because it moved Plugin Ingestion's failure and lifetime
  responsibilities across the seam.
- An explicit application-owned master-style request was rejected because it recreated the shallow snapshot using new
  transport types.

## Consequences

- File availability remains distinct from Game Load Order membership in the domain model, but the module that observes
  both facts owns their coordination for its two caller roles.
- The production Game Load Orders and overlay adapters are a matched pair through the opaque read capability;
  composition and adapter-contract tests must reject a mismatched capability before overlay construction begins.
- Processing Run remains independent of Game Load Orders, canonicalization, and overlay adapters.
- Caller tests no longer inspect the Boolean mode, master styles, or binary read parameters. Empty-versus-absent lookup
  behavior remains covered at the production environment adapter and through real-overlay ADR-0006 fixtures.
