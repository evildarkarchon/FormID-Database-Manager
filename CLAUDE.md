# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FormID Database Manager is a cross-platform desktop application built with Avalonia UI that creates SQLite databases containing FormIDs and their associated EditorID/Name values from Bethesda game plugins. It supports Skyrim (SE/AE/VR/GOG), Fallout 4, Starfield, and Oblivion.

## Build Commands

```bash
# Build the project
dotnet build

# Run the application
dotnet run

# Publish for release
dotnet publish -c Release
```

## Architecture

The application follows MVVM pattern with these key components:

### Services (Business Logic)
- **DatabaseService**: Manages SQLite database operations with game-specific tables
- **GameDetectionService**: Auto-detects game type from plugin directory
- **ModProcessor**: Processes individual plugins using Mutagen library
- **PluginProcessingService**: Orchestrates the entire processing workflow
- **FormIdTextProcessor**: Filters FormID text files based on plugin lists
- **PluginListManager**: Manages plugin list loading and parsing
- **WindowManager**: Window positioning and management utilities

### Key Design Patterns
- Dependency injection for services
- Async/await throughout for UI responsiveness
- Error callbacks with ignorable error patterns
- Batch database operations for performance
- Custom assembly resolver for DLL loading from libs folder

### Database Schema
Each game gets its own table:
```sql
CREATE TABLE {GameRelease} (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    plugin TEXT NOT NULL,
    formid TEXT NOT NULL,
    entry TEXT NOT NULL
)
```

## Important Implementation Details

1. **DLL Resolution**: Program.cs contains custom assembly resolver that loads DLLs from `libs` folder
2. **Game Detection**: Uses directory name and plugin files to auto-detect game type
3. **Batch Processing**: Database inserts are batched (1000 entries) for performance
4. **Error Handling**: Ignorable errors are defined in GameDetectionService for known issues
5. **UI Threading**: Heavy operations use Task.Run to avoid blocking UI
6. **Acrylic Effect**: MainWindow uses platform-specific acrylic blur for modern appearance