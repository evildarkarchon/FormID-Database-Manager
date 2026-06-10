## Why

Phase 8 removed Avalonia and left the WinUI app as the only desktop shell, so the project now needs release-ready publish paths instead of migration-era build commands. Phase 9 should make packaged and unpackaged deployment choices explicit, avoid unsafe default output locations, and document how to produce and verify release artifacts.

## What Changes

- Add release publish support for the WinUI project without disabling packaged MSIX capability in the base project.
- Add explicit packaged and unpackaged publish profiles or documented commands covering MSIX output and framework-dependent or self-contained unpackaged output.
- Keep `WindowsPackageType=None` scoped only to unpackaged publish profiles or commands.
- Define the packaged distribution decision point, including signing and update-flow handling for the selected channel.
- Fix default writable database output behavior so packaged launches do not default to `C:\WINDOWS\system32` or another elevated-permission location.
- Update README build, run, and publish instructions for the WinUI project.
- Record verification expectations for both release lanes, including clean Windows machine or VM validation.

## Capabilities

### New Capabilities
- `winui-release-publishing`: Release publishing behavior for the WinUI application, including packaged and unpackaged publish lanes, writable output defaults, documentation, and release verification.

### Modified Capabilities
- None.

## Impact

- WinUI project publish configuration and any publish profiles under `FormID Database Manager.WinUI`.
- Default database path selection in WinUI/Core workflow code.
- README and migration documentation for build, run, publish, and verification commands.
- CI or local verification commands used to validate release output.
