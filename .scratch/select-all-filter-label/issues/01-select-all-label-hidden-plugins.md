# Say that Select All includes Plugins hidden by the text filter

Status: resolved

## Problem

Select All and Select None apply to the complete Confirmed Plugin List, including Plugins the text filter currently
hides. That is intended and pinned by
`UserWorkflowTests.SelectAllAndNonePlugins_ActiveFilter_ApplyToCompleteConfirmedMembership`
(`FormID Database Manager.Tests/Unit/Services/UserWorkflowTests.cs:1896-1915`).

Nothing on screen says so. The button reads only "Select All" (`FormID Database Manager.WinUI/MainWindow.xaml:195`)
with no tooltip or `AutomationProperties.HelpText`, while the filter's help text is "Filter the plugin list by plugin
name." (`MainWindow.xaml:171`). A user who filters, presses Select All, and starts a Processing Run can ingest Plugins
they never saw selected.

## Scope

Presentation wording only. The bulk-selection behavior itself is not in question.

## Found by

Architecture review of 2026-10-13 (Plugin List candidate), while checking whether bulk selection ignoring the filter was
a defect. It is not; only its labelling is.

## Completion

Select All and Select None now carry matching `ToolTipService.ToolTip` and `AutomationProperties.HelpText` in
`MainWindow.xaml`: "Select every plugin, including plugins hidden by the filter." and "Clear every plugin selection,
including plugins hidden by the filter." The visible button labels and the filter's help text are unchanged.
`WinUiPlatformServiceSourceTests.WinUiMainWindow_BulkSelectionButton_StatesThatItIncludesFilteredOutPlugins` pins both
attributes on each button.
