# FormID Database Manager

FormID Database Manager is a desktop application developed using [Avalonia](https://avaloniaui.net/) that provides a
robust and user-friendly interface for creating SQLite databases that store the FormIDs and their associated
EditorID or Name value (if any) from Bethesda game plugins.

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
- **Avalonia UI 11.3**: Cross-platform desktop UI framework
- **Mutagen**: Bethesda plugin parsing library
- **Microsoft.Data.Sqlite**: SQLite database access
- **xUnit** + **Moq**: Testing framework

---

## Getting Started

### Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later

### Build

```bash
dotnet build "FormID Database Manager.slnx"
```

### Run

```bash
dotnet run --project "FormID Database Manager"
```

### Test

```bash
dotnet test "FormID Database Manager.Tests"
```

### Publish

```bash
dotnet publish "FormID Database Manager" -c Release -r win-x64
```

This produces a self-contained, trimmed single-file executable.

---

## Project Structure

- **`FormID Database Manager/`**: Main application
  - `Program.cs` — Application entry point
  - `App.axaml` / `App.axaml.cs` — Avalonia application core and styles
  - `MainWindow.axaml` / `MainWindow.axaml.cs` — Main UI window
  - `ViewModels/` — MVVM view models
  - `Services/` — Business logic (database, plugin processing, game detection)
  - `Models/` — Data models
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

- [Avalonia UI](https://avaloniaui.net/) for providing the framework to build a cross-platform desktop application.
- [Mutagen](https://github.com/Mutagen-Modding/Mutagen) for Bethesda plugin parsing.

---

## Contact

For inquiries or support, please contact me at `evildarkarchon@gmail.com`.
