# FormID Database Manager

FormID Database Manager is a desktop application developed using [Avalonia](https://avaloniaui.net/) that aims to
provide a robust and user-friendly interface for creating SQLite databases that store the FormIDs and their associated
EditorID or Name value (if any).

---

## Technical Overview

### Framework and Technologies

- **C#**: Built with `C# 12.0` for clean, efficient, and modern software development.
- **Avalonia UI**: Provides cross-platform UI development with advanced rendering capabilities.
- **Mutagen Framework**: Allows parsing of Bethesda Plugins to get the data needed for the database.
- **Target Framework**: `.NET 8.0`.

---

## Getting Started

### Prerequisites

To run the project, ensure you have:

- .NET SDK 8.0 or later installed.

---

## Project Structure

- **`Program.cs`**: Handles application entry point and configuration.
- **`App.xaml` / `App.xaml.cs`**: Defines and initializes the Avalonia application core and styles.
- **UI Components**: Contains Avalonia XAML files and related logic for user interface rendering and interaction.
- **`libs` Directory**: Holds dynamically loaded `.dll` dependencies.

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

---

## Contact

For inquiries or support, please contact me at `evildarkarchon@gmail.com`.
