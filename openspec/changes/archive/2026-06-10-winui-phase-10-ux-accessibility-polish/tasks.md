## 1. Layout and Theme Audit

- [x] 1.1 Inspect `MainWindow.xaml` and `App.xaml` for fixed widths, unconstrained panels, hard-coded colors, missing text wrapping/trimming, and controls that may clip at medium or narrow widths.
- [x] 1.2 Define representative wide, medium, and minimum narrow window widths for Phase 10 verification and record the chosen breakpoints in implementation notes.
- [x] 1.3 Identify controls that need explicit accessibility metadata, label relationships, help text, tab-order adjustments, or access keys.

## 2. Responsive WinUI Layout

- [x] 2.1 Rework the main field layout so labels, path fields, detected-directory selection, and picker buttons reflow without overlap at the selected medium and narrow widths.
- [x] 2.2 Rework plugin-list commands, mode toggles, progress, process action, and message areas so they remain reachable and readable when width or text size is constrained.
- [x] 2.3 Preserve a virtualization-friendly native `ListView` structure for plugins while ensuring the list remains height-constrained and scrollable.
- [x] 2.4 Keep visual styling resource-driven through WinUI theme resources and native Fluent control states for light, dark, and high contrast modes.

## 3. Accessibility and Keyboard Operation

- [x] 3.1 Add accessible names, label relationships, or help text for game selection, directory controls, database path, FormID list path, plugin filter, plugin list, progress, and message surfaces where native content is insufficient.
- [x] 3.2 Add access keys or tab-order adjustments for primary commands only where default WinUI keyboard navigation is insufficient or hard to follow.
- [x] 3.3 Verify keyboard activation for Browse, Select Database, Select List File, Select All, Select None, Process FormIDs, plugin checkbox toggling, plugin-list scrolling, and processing cancellation.
- [x] 3.4 Ensure text scaling remains enabled and long paths, plugin names, progress text, and messages wrap, trim, scroll, or constrain gracefully.

## 4. Automated Guardrails

- [x] 4.1 Add or update WinUI source tests that guard responsive layout resources, named controls, bindings, and event handlers after the XAML rework.
- [x] 4.2 Add or update source tests for automation metadata, label relationships, access keys or tab-order decisions, and absence of broad `IsTextScaleFactorEnabled="False"` usage.
- [x] 4.3 Add or update guardrails that verify the plugin list remains a native virtualization-friendly list and is not placed inside an unconstrained scrolling-breaking parent.
- [x] 4.4 Add or update theme-resource checks to prevent hard-coded app-surface colors from replacing WinUI theme resources.

## 5. Verification and Documentation

- [x] 5.1 Run `dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64`.
- [x] 5.2 Run `dotnet build "FormID Database Manager.slnx"`.
- [x] 5.3 Run `dotnet test "FormID Database Manager.Tests"` and report skipped/manual tests using existing project conventions.
- [x] 5.4 Verify the WinUI shell at the selected wide, medium, and narrow widths, including absence of overlap and reachability of primary controls.
- [x] 5.5 Verify light, dark, and high contrast theme behavior, including text readability, focus indicators, plugin selection state, messages, and progress state.
- [x] 5.6 Verify keyboard-only operation and UI Automation metadata with local tools or record exact tooling/environment blockers.
- [x] 5.7 Verify enlarged text scaling and representative long path/plugin-name behavior.
- [x] 5.8 Verify long plugin-list scrolling and filtering responsiveness with a representative large list or record the exact environment blocker.
- [x] 5.9 Update `docs/WinUI-Migration-Plan.md` with the Phase 10 implementation and verification checkpoint.
