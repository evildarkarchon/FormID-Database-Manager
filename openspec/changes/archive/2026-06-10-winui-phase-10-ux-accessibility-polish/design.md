## Context

The WinUI shell is now the supported desktop app, and Phase 9 documented release lanes plus current launch blockers. The main window uses native WinUI controls and theme resources, but the current layout is still a migration-parity surface: fixed label columns, fixed minimum path textbox widths, right-side action buttons, and no explicit automation metadata beyond visible text.

Phase 10 is the final polish pass for people actually using the app. It needs to keep the current processing workflow intact while improving layout resilience, keyboard access, screen-reader metadata, theme behavior, text scaling, and long-list responsiveness. Microsoft accessibility guidance frames this around programmatic access, keyboard navigation, and color/contrast, and WinUI responsive-layout guidance favors fluid XAML panels plus visual states for meaningful window-size breakpoints.

## Goals / Non-Goals

**Goals:**

- Make the single-window workflow usable at representative wide, medium, and narrow window widths.
- Preserve native WinUI/Fluent controls and theme resources for light, dark, and high contrast modes.
- Ensure all primary flows can be completed with keyboard navigation and activation.
- Expose clear UI Automation names, labels, and helpful descriptions for controls whose visible content is not enough.
- Keep text scaling enabled and prevent clipping of labels, paths, progress text, plugin names, and action buttons.
- Verify long plugin lists continue to scroll smoothly and filter responsively.
- Record Phase 10 verification in the migration plan.

**Non-Goals:**

- Redesign the product into a multi-page app or change the processing workflow.
- Add custom title-bar chrome, custom controls, or custom automation peers unless native controls cannot satisfy a verified accessibility gap.
- Replace the current ViewModel architecture or service orchestration.
- Resolve Phase 9 release signing/runtime blockers unless a polish change directly exposes them.
- Add broad desktop UI automation infrastructure beyond focused checks that can be maintained.

## Decisions

### Keep the shell native and resource-driven

Use built-in WinUI controls, existing `ThemeResource` brushes, and app-level styles before adding custom visual treatment. Theme-specific changes should live in `App.xaml` resources or control properties, not in hard-coded colors. High contrast support should rely on WinUI resource resolution wherever possible.

Alternative considered: add custom colors and chrome for a more distinctive visual pass. That would increase accessibility risk late in the migration and conflicts with the Phase 10 requirement to keep Windows-native controls and Fluent styling as the default.

### Use fluid layout first, then targeted visual states

Remove avoidable fixed-width pressure from the main workflow before adding breakpoints. The wide layout can keep label/input/action columns; medium and narrow layouts should reflow actions beneath their related fields or stack workflow sections when columns would clip text or paths. XAML visual states or a small size-change handler may be used when simple star/auto sizing is not enough.

Alternative considered: only increase the minimum window size. That hides clipping instead of making the app resilient, and it does not satisfy the narrow-width verification requirement.

### Prefer explicit labels and automation properties over hidden accessibility-only code

Visible field labels should be named XAML elements and connected to their controls with `AutomationProperties.LabeledBy` where WinUI supports it. Buttons and checkboxes with clear visible text can keep their implicit names, while path text boxes, plugin filter, plugin list, progress, and message surfaces should receive explicit names or help text if assistive tooling cannot infer purpose.

Alternative considered: add custom automation peers. Native WinUI controls already expose roles and patterns; custom peers should be reserved for a verified gap after inspection.

### Keep keyboard behavior close to default WinUI conventions

The design should use tab order, access keys, focusable controls, and built-in ListView/CheckBox keyboard behavior instead of bespoke key handling. Add code-behind only when a primary workflow cannot be reached or activated by standard keyboard navigation.

Alternative considered: implement global accelerator keys for most commands. That increases discoverability and conflict risk; Phase 10 can add a small set only where it helps repeated workflows without surprising users.

### Treat responsiveness as both layout and data interaction

Long plugin lists are part of the real workload. Verification should cover scrolling and filtering with large lists, not just static rendering. Keep the `ListView` virtualization-friendly and avoid wrapping it in unconstrained panels that disable virtualization or scrolling.

Alternative considered: replace the plugin list with a more customized selector. The existing native `ListView` plus checkbox template is sufficient if layout constraints preserve virtualization.

## Risks / Trade-offs

- Responsive layout changes could break existing bindings or event handlers -> Add source-level guardrails for named controls, bindings, handlers, and automation properties.
- Text scaling and narrow widths may still require scrolling -> Allow vertical scrolling for the full workflow, but verify primary actions remain reachable and controls do not overlap or clip incoherently.
- High contrast visual issues can be hard to prove in unit tests -> Combine resource/source checks with manual or UI automation verification in the Phase 10 checkpoint.
- Keyboard-only picker verification depends on native Windows picker dialogs -> Verify the app-side buttons, focus order, and cancellation/return behavior; record any OS dialog automation limits.
- Long-list smoothness is machine-sensitive -> Use representative large-list smoke checks and avoid hard performance thresholds unless a stable automated measurement already exists.

## Migration Plan

1. Audit `MainWindow.xaml` and `App.xaml` for fixed sizing, theme resources, tab order, and automation metadata gaps.
2. Rework layout resources and main-window structure so fields, plugin controls, progress, messages, and actions reflow across wide, medium, and narrow widths.
3. Add accessibility metadata and access-key/tab-order adjustments where native defaults are insufficient.
4. Add or update focused source tests for responsive resources, automation labels, keyboard hooks, theme-resource use, and virtualization-preserving plugin-list structure.
5. Run build and automated tests, then perform manual or UI automation checks for layout widths, light/dark/high contrast, keyboard-only flow, text scaling, and long plugin-list behavior.
6. Record Phase 10 implementation and verification results in `docs/WinUI-Migration-Plan.md`.

Rollback is low risk because Phase 10 should stay mostly in XAML/resources and narrow code-behind adjustments. If a layout change destabilizes workflow behavior, revert the affected layout section while keeping independent accessibility metadata improvements.

## Open Questions

- Which exact narrow-width breakpoint should be treated as the minimum supported window width after implementation?
- Should Phase 10 add access keys for every primary command, or only for the most repeated actions such as Browse, Select Database, Select List File, Select All, Select None, and Process?
- Can local tooling reliably automate high contrast and text-scaling checks, or should those remain documented manual verification for this repository?
