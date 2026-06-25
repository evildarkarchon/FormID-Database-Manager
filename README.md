# FormID Database Manager

FormID Database Manager is a WinUI desktop application that provides a robust and user-friendly interface for creating
SQLite databases that store the FormIDs and their associated EditorID or Name value (if any) from Bethesda game plugins.

## Supported Games

- The Elder Scrolls IV: Oblivion
- The Elder Scrolls V: Skyrim (LE, SE, SE GOG, VR)
- Enderal (LE, SE)
- Fallout 4 (including VR)
- Starfield

## Features

- Automatic game detection from directory structure
- Plugin list loading with filtering and selection
- Batch processing of plugins into a SQLite database
- Import from pipe-delimited FormID text files (`plugin|formid|entry`)
- Update mode to replace existing plugin entries
- Progress reporting with cancellation support

---

## Technical Overview

### Framework and Technologies

- **C#** / **.NET 10.0**
- **WinUI 3 / Windows App SDK**: Windows desktop UI framework
- **CommunityToolkit.Mvvm**: MVVM source generators and observable state
- **Mutagen**: Bethesda plugin parsing library
- **Microsoft.Data.Sqlite**: SQLite database access
- **xUnit** + **Moq**: Testing framework

---

## WinUI Migration Checkpoint

Phase 0 was recorded on June 9, 2026 before extracting the UI-neutral core boundary.

- Branch/checkpoint: `winui`
- Worktree state: existing uncommitted OpenSpec/migration-plan changes were present before Phase 1 extraction work began.
- Baseline build: `dotnet build "FormID Database Manager.slnx"` succeeded with `0` warnings and `0` errors.
- Baseline tests: `dotnet test "FormID Database Manager.Tests"` passed with `272` passed and `11` skipped.
- WinUI template status: `dotnet new list winui` exposes the C# `WinUI 3 App`, `WinUI 3 Blazor App`, and `WinUI 3 Class Library` templates.
- Target deployment model: unpackaged self-contained portable WinUI app. The WinUI project is now the supported desktop shell.

---

## Getting Started

### Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later
- Windows 10/11 for default solution and WinUI desktop shell builds

### Build

Default solution builds are intentionally Windows-only because the active desktop shell targets WinUI and the Windows App SDK. Linux/macOS solution builds are unsupported.

```bash
dotnet build "FormID Database Manager.slnx"
```

Build only the WinUI desktop shell:

```bash
dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -p:Platform=x64
```

### Run

Debug CLI runs use the WinUI project's unpackaged self-contained defaults so the app can launch directly from `dotnet run`.

```bash
dotnet run --project "FormID Database Manager.WinUI" -p:Platform=x64
```

### Test

```bash
dotnet test "FormID Database Manager.Tests"
```

### Coverage (Machine + Human Readable)

```powershell
pwsh ./scripts/run-coverage.ps1
```

This generates:
- Cobertura XML: `coverage/coverage.cobertura.xml`
- JSON: `coverage/coverage.json`
- HTML report: `coverage/report/index.html`

Optional (open the HTML report automatically):

```powershell
pwsh ./scripts/run-coverage.ps1 -OpenReport
```

### Publish

Portable self-contained, x64 (primary):

```powershell
pwsh ./scripts/publish-portable.ps1
```

Underlying build command:

```powershell
dotnet build "FormID Database Manager.WinUI\FormID Database Manager.WinUI.csproj" -c Release -p:Platform=x64
```

Runtime and distribution notes:

- Portable output carries the .NET and Windows App SDK runtimes with the app; no installer or separate runtime installation is required.
- Unzip the portable package and run `FormID Database Manager.WinUI.exe` from the extracted folder.
- The app is unsigned, so Windows SmartScreen warnings are expected until code signing is configured.
- Single-file publishing is intentionally not provided because the Windows App SDK single-file lane requires MSIX tooling for WinUI self-extraction validation.
- When no database path is selected, generated databases are written under `%LOCALAPPDATA%\FormID Database Manager\Databases`.

---

## Project Structure

- **`FormID Database Manager.Core/`**: UI-neutral application core
  - `Models/` — Data models
  - `ViewModels/` — MVVM view models
  - `Services/` — Business logic, dispatcher/file-dialog abstractions, database, plugin processing, and game detection
- **`FormID Database Manager.WinUI/`**: Supported WinUI desktop application shell
  - `App.xaml` / `App.xaml.cs` — WinUI application startup and resources
  - `MainWindow.xaml` / `MainWindow.xaml.cs` — Main UI window and workflow event handlers
  - `Services/` — WinUI dispatcher and picker implementations
- **`FormID Database Manager.Tests/`**: Unit, integration, UI, and performance tests
- **`FormID Database Manager.TestUtilities/`**: Shared test fixtures, mocks, and builders

---

## Contributing

Contributions are welcome! Please follow the steps below to contribute:

1. Fork the repository.
2. Create your branch (`git checkout -b feature/new-feature`).
3. Commit your changes (`git commit -m "Add new feature"`).
4. Push to the branch (`git push origin feature/new-feature`).
5. Open a pull request.

---

## License

This project is licensed under the [GPL-3.0 License](https://www.gnu.org/licenses/gpl-3.0.en.html). Feel free to use,
modify, and distribute the code as needed.

---

## Acknowledgements

- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/) for the WinUI desktop application framework.
- [Mutagen](https://github.com/Mutagen-Modding/Mutagen) for Bethesda plugin parsing.

---

## Contact

For inquiries or support, please contact me at `evildarkarchon@gmail.com`.
