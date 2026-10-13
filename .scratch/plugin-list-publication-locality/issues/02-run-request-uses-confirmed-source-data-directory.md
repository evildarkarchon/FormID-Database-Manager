# 02: Take the run request's Data directory from the Confirmed Plugin List

**What to build:** In User Workflow request creation, build the selected-Plugin Processing Run request's game directory
from the matched Confirmed Plugin List's `Source.DataDirectory` instead of re-creating a `PluginListSource` from the
captured Game Context (`UserWorkflow.cs:742-744`). See `../spec.md`.

**Blocked by:** None (can start immediately; independent of 01).

**Status:** ready-for-agent

- [ ] The selected-Plugin request's game directory is `confirmedPluginList.Source.DataDirectory`; the second
      `PluginListSource.Create` call in `CreateProcessingRunRequest` is removed.
- [ ] `GetConfirmedPluginListFor` stays in User Workflow and still matches the Confirmed Plugin List against the
      captured Game Context's source before it is used.
- [ ] The unconfirmed-request branch that preserves legacy validation order is unchanged.
- [ ] Accepted difference (confirmed by the maintainer, 2026-10-13): `PluginListSource` equality ignores path case on Windows, so the matched confirmation's
      canonical Data directory can differ in letter case from one re-created from the captured Game Context. The
      request now carries the casing of the directory the Plugin List was actually discovered from. Plugin Ingestion
      canonicalizes again and Windows file access is case-insensitive, so ingestion is unaffected; if an existing
      test pins the re-created casing, record which and why in the completion note rather than silently editing it.
- [ ] Existing `UserWorkflowTests` and Processing Run tests pass.
- [ ] The supported build sequence and the non-performance test suite pass.
