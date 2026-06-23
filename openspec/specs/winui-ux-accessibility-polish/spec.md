## Purpose

Define final WinUI layout, theme, keyboard, accessibility, text-scaling, and large-list polish requirements.
## Requirements
### Requirement: WinUI main window adapts across supported widths
The system SHALL keep the primary WinUI workflow usable at representative wide, medium, and narrow desktop window widths.

#### Scenario: Wide layout preserves efficient scanning
- **WHEN** the WinUI main window is displayed at a wide desktop width
- **THEN** game selection, path selection, plugin selection, mode toggles, progress, messages, and process controls are visible in a layout optimized for scanning and repeated use

#### Scenario: Medium layout avoids clipped workflow controls
- **WHEN** the WinUI main window is resized to a medium desktop width
- **THEN** field labels, path controls, picker buttons, plugin commands, progress text, and process controls remain reachable without incoherent overlap or clipped button text

#### Scenario: Narrow layout reflows instead of overlapping
- **WHEN** the WinUI main window is resized to the minimum supported narrow width
- **THEN** the workflow reflows or scrolls so all primary controls remain reachable
- **AND** no visible controls overlap each other in a way that prevents use

### Requirement: WinUI theme and contrast modes remain readable
The system SHALL use WinUI theme resources and native control states so the shell remains readable and usable in light, dark, and high contrast modes.

#### Scenario: Light and dark themes use theme resources
- **WHEN** the WinUI shell is inspected or rendered in light and dark themes
- **THEN** page backgrounds, text, borders, message surfaces, plugin list surfaces, and focus states use WinUI theme resources or native control styling instead of hard-coded light-only or dark-only colors

#### Scenario: High contrast mode remains usable
- **WHEN** the WinUI shell is rendered with a Windows high contrast theme active
- **THEN** text, controls, focus indicators, plugin selection state, messages, and progress state remain distinguishable and usable

#### Scenario: Color is not the only status signal
- **WHEN** the WinUI shell reports errors, information, progress, selection, or cancellation state
- **THEN** the state is available through text, control state, or UI Automation metadata rather than color alone

### Requirement: Primary workflows support keyboard-only operation
The system SHALL allow users to complete primary WinUI workflows without a mouse.

#### Scenario: Tab order follows workflow order
- **WHEN** keyboard focus starts at the top of the WinUI main window and the user tabs forward through controls
- **THEN** focus moves through game selection, directory selection, database selection, FormID list selection, plugin filtering, plugin list, plugin commands, mode toggles, progress action, and messages in a coherent workflow order

#### Scenario: Picker and command buttons are keyboard activatable
- **WHEN** keyboard focus is on Browse, Select Database, Select List File, Select All, Select None, or Process FormIDs
- **THEN** pressing Space or Enter invokes the same command as clicking the button

#### Scenario: Plugin list supports keyboard selection
- **WHEN** keyboard focus is in the plugin list
- **THEN** the user can move through visible plugins, toggle plugin checkboxes, and scroll the list using standard keyboard interactions

#### Scenario: Active processing can be cancelled from the keyboard
- **WHEN** processing is active and keyboard focus reaches the process button
- **THEN** the user can invoke the cancel action from the keyboard and receive visible cancellation feedback

### Requirement: Assistive technologies receive useful control metadata
The system SHALL expose accessible names, label relationships, and helpful descriptions for WinUI controls whose purpose is not sufficiently conveyed by native control content.

#### Scenario: Form fields expose label relationships
- **WHEN** UI Automation inspects game, game directory, detected directory, database path, FormID list path, and plugin filter controls
- **THEN** each control exposes a meaningful accessible name or label relationship matching the visible field purpose

#### Scenario: Plugin list exposes purpose and item names
- **WHEN** UI Automation inspects the plugin list and visible plugin checkboxes
- **THEN** the list exposes its purpose
- **AND** each visible plugin checkbox exposes the plugin name and checked state

#### Scenario: Progress and messages expose current state
- **WHEN** UI Automation inspects progress and message surfaces after status changes
- **THEN** progress text, progress value, error messages, and information messages expose their current user-visible state to assistive tooling

### Requirement: Text scaling does not break the workflow
The system SHALL preserve readability and reachability when Windows text scaling or display scaling increases text size.

#### Scenario: Text scaling remains enabled
- **WHEN** the WinUI source is inspected after Phase 10
- **THEN** text scaling is not broadly disabled on labels, buttons, path fields, plugin items, progress text, or message text

#### Scenario: Enlarged text remains readable
- **WHEN** the WinUI shell is verified with enlarged Windows text settings or an equivalent text-scaling test configuration
- **THEN** labels, button content, picker paths, plugin names, progress text, and messages remain readable without incoherent overlap

#### Scenario: Long paths and plugin names degrade gracefully
- **WHEN** game paths, database paths, FormID list paths, or plugin names exceed the available visible width
- **THEN** the UI wraps, trims, scrolls, or otherwise constrains the text without resizing the workflow into an unusable state

### Requirement: Long plugin lists remain responsive
The system SHALL preserve smooth scrolling and responsive filtering for long plugin lists in the WinUI shell.

#### Scenario: Plugin list virtualization remains enabled
- **WHEN** the WinUI plugin list structure is inspected after Phase 10
- **THEN** it remains a virtualization-friendly native list control and is not wrapped in an unconstrained layout that disables scrolling or virtualization

#### Scenario: Large plugin list remains scrollable
- **WHEN** a representative long plugin list is loaded in the WinUI shell
- **THEN** the user can scroll through the list without the rest of the workflow becoming unusable

#### Scenario: Filtering remains responsive for long lists
- **WHEN** the user types into the plugin filter with a representative long plugin list loaded
- **THEN** visible results update through the existing dispatcher-backed filtering behavior without losing selected plugin state

### Requirement: Phase 10 polish is verified and documented
The system SHALL verify and document the final UX and accessibility polish pass before the WinUI migration is considered complete.

#### Scenario: Automated verification remains green
- **WHEN** Phase 10 verification is run
- **THEN** the WinUI project build, full solution build, and current automated test suite pass or report skipped/manual tests using existing project conventions

#### Scenario: Accessibility and layout checks are recorded
- **WHEN** Phase 10 verification is complete
- **THEN** `docs/WinUI-Migration-Plan.md` records layout-width, theme, high contrast, keyboard-only, automation metadata, text-scaling, and long-plugin-list verification results or exact blockers

#### Scenario: Native-control constraint is preserved
- **WHEN** Phase 10 implementation is reviewed
- **THEN** any custom control, custom chrome, or custom automation peer introduced by the change has a documented verified gap that native WinUI controls could not satisfy
