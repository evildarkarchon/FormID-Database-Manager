## Context

The staged WinUI shell now has behavior parity for the primary workflows, but some UI reliability still depends on details that differ between Avalonia and WinUI. The WinUI window currently uses runtime `Binding` for most ViewModel state, explicit `HasErrorMessages` and `HasInformationMessages` properties for `InfoBar.IsOpen`, a small event-handler mutation for process button content, and `MainWindowViewModel` dispatcher checks around filtering and message/progress updates.

Phase 6 should keep this work narrow. It is a binding and ViewModel hardening pass before Phase 7 test rework and Phase 8 Avalonia removal, not a layout redesign or workflow rewrite.

## Goals / Non-Goals

**Goals:**

- Make WinUI binding choices intentional and reviewable for template data, dynamic ViewModel state, and any strongly typed binding candidates.
- Ensure message visibility properties raise notifications when message collection contents change, not only when collection properties are replaced.
- Decide how `PluginListItem` validation should surface in the WinUI shell and add the smallest support needed for consistent user-visible behavior.
- Move process button text/content to a reliable state-driven pattern or keep the existing handler scoped and covered by tests.
- Verify debounced plugin filtering continues to marshal UI-bound collection mutations through the WinUI dispatcher.
- Record Phase 6 verification in the migration plan.

**Non-Goals:**

- Removing Avalonia dependencies from tests or app projects; that remains Phase 8 after test rework.
- Replacing the current WinUI layout, controls, packaging model, or processing workflow.
- Introducing a new validation framework or dependency.
- Expanding automated UI coverage beyond focused source, ViewModel, dispatcher, or smoke checks needed for binding-critical behavior.

## Decisions

1. Keep runtime `Binding` as the default for ViewModel-driven WinUI state.

   Runtime `Binding` works naturally with `DataContext`, item templates, observable collections, and ViewModel property changes. `x:Bind` should only be introduced where the source is strongly typed, not inside untyped item templates or dynamic collection bindings. The alternative was to convert broadly to `x:Bind`, but that would add code-behind surface and make template data contexts easier to break during a migration phase whose goal is reliability.

2. Treat message visibility as ViewModel state, not XAML collection-count logic.

   `HasErrorMessages` and `HasInformationMessages` are the right abstraction for WinUI `InfoBar.IsOpen`, because WinUI binding to nested collection counts is brittle and does not express intent. The implementation should keep collection change subscriptions attached when message collections are replaced and raise property changes when items are added, removed, or cleared. The alternative was a XAML converter or direct `Count` binding, but that would reintroduce framework-specific glue for a simple state question.

3. Prefer an explicit process button state property if button content needs more than a tiny local handler.

   The current event handler updates `ProcessFormIdsButton.Content` directly on start and reset. If Phase 6 keeps this approach, tests should cover that it remains narrow and paired with `IsProcessing`; if implementation complexity grows, add a ViewModel property such as `ProcessButtonText` derived from processing state and bind the button content to it. The alternative is leaving button text as an uncovered side effect, which is the behavior most likely to drift during later test and Avalonia removal work.

4. Audit `IDataErrorInfo` rather than assuming WinUI will mirror Avalonia validation.

   `PluginListItem` implements `IDataErrorInfo`, but the current WinUI plugin list only binds checkbox content to `Name`, so invalid plugin names may not surface any validation visuals. Phase 6 should either document that plugin item validation is not user-facing in the current WinUI shell or add explicit binding/test support where validation is expected. The alternative is preserving an interface that appears meaningful but has no verified WinUI effect.

5. Keep dispatcher ownership in `MainWindowViewModel` for filtering and message/progress updates.

   The existing ViewModel already posts filter and UI-bound collection work through `IThreadDispatcher` when it lacks access. Phase 6 should add focused coverage for debounced filtering with a dispatcher that can prove posting happened, instead of moving filtering into the WinUI code-behind. The alternative would split filtering behavior between core and WinUI and make Phase 7 test migration harder.

## Risks / Trade-offs

- Binding audit misses a runtime-only issue -> Mitigation: combine source-level assertions with a packaged WinUI smoke/manual verification checklist focused on message bars, filtering, selection, and processing button transitions.
- Process button state remains in code-behind -> Mitigation: keep the handler mutation localized and covered, or promote it to a ViewModel property if additional state transitions are needed.
- `IDataErrorInfo` has no visible WinUI effect -> Mitigation: make that an explicit finding and either add a simple user-visible validation path or update the validation contract to reflect current behavior.
- Dispatcher tests become tied to implementation details -> Mitigation: assert observable posting and UI-bound collection results rather than private method names.

## Migration Plan

1. Audit `MainWindow.xaml` and `MainWindow.xaml.cs` for binding modes, `x:Bind` candidates, message `InfoBar` bindings, and process button content handling.
2. Add or update focused tests for message visibility notifications, process button state handling, plugin item validation expectations, and debounced filter dispatcher posting.
3. Make the smallest code changes needed to satisfy the tests.
4. Build the WinUI project, build the full solution, run the focused tests, then run the full test suite if practical.
5. Add a Phase 6 verification checkpoint to `docs/WinUI-Migration-Plan.md`.

## Open Questions

- Should process button content become a ViewModel property during Phase 6, or is the existing event-handler update acceptable once covered?
- Should invalid `PluginListItem.Name` ever be user-visible in the WinUI shell, or is `IDataErrorInfo` only a defensive model-level guard for non-UI consumers?
