## Why

The WinUI migration is functionally complete enough to shift from parity and publishing work to final user-facing quality. Phase 10 should make the desktop shell comfortable, accessible, theme-correct, and resilient across realistic window sizes before the migration is treated as complete.

## What Changes

- Audit and adjust the WinUI main window at wide, medium, and narrow widths so primary workflows remain usable without overlapping or clipped controls.
- Support light, dark, and high contrast themes through WinUI resources and native control states.
- Ensure keyboard-only operation covers game selection, directory/database/FormID pickers, plugin filtering and selection, mode toggles, scrolling, and processing/cancellation.
- Add useful automation names or labels for controls whose purpose is not already exposed clearly to accessibility tooling.
- Verify text scaling for labels, paths, progress text, plugin names, and button content.
- Confirm long plugin lists remain smooth and responsive during scrolling and filtering.
- Preserve Windows-native Fluent controls and avoid custom chrome or custom controls unless implementation verifies a specific gap.

## Capabilities

### New Capabilities
- `winui-ux-accessibility-polish`: Final WinUI user-experience, accessibility, responsive-layout, theme, text-scaling, and list-responsiveness requirements.

### Modified Capabilities
- None.

## Impact

- `FormID Database Manager.WinUI/MainWindow.xaml` layout, resources, bindings, and accessibility properties.
- `FormID Database Manager.WinUI/MainWindow.xaml.cs` only where keyboard workflow, focus management, or verification hooks require code-behind changes.
- Core ViewModel properties only if accessible labels, state exposure, or responsive behavior need UI-neutral support.
- WinUI source tests, smoke/manual verification notes, and `docs/WinUI-Migration-Plan.md` Phase 10 checkpoint documentation.
