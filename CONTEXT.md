# FormID Database Manager Context

This context covers building a searchable database of FormID records from Bethesda game plugins and FormID text files.

## Language

**FormID**:
A hexadecimal identifier for a record inside a Bethesda game plugin. A FormID is stored together with the Plugin it came from and a human-readable Entry.

**Plugin**:
A Bethesda game data file selected for processing, such as an `.esm`, `.esp`, or `.esl` file.
_Avoid_: Mod, except when referring to Mutagen type names.

**Skipped Plugin**:
A selected Plugin that a Processing Run did not store records for because it was not present in the load order, its file was unavailable, or it produced zero FormID records. It is reported as a warning and does not count as either successfully processed or failed.
_Avoid_: Successful plugin, missing file error, ignored plugin.

**Failed Plugin**:
A selected Plugin whose ingestion could not complete because of a fatal Plugin-specific error, not because of a FormID Record Store failure. A Failed Plugin counts against the Processing Run summary, but does not by itself mean the whole Processing Run failed.
_Avoid_: Run failure, warning, skipped plugin.

**Plugin List**:
The loaded set of Plugins available for a selected GameRelease and game directory, including the user's current Plugin selection.
_Avoid_: Mod list, file list.

**Plugin Ingestion**:
The part of a Processing Run that reads selected Plugins and produces FormID records for a FormID Record Store.
_Avoid_: Mod processing, plugin processing.

**Advanced Mode**:
A Plugin List display mode that includes base game Plugins that are normally hidden from Plugin selection.
_Avoid_: Show all, expert mode.

**GameRelease**:
The target Bethesda game or edition that determines plugin layout, base plugins, and database table selection.
_Avoid_: Game type, release enum.

**Game Context**:
The User Workflow state that determines which Plugin List can be loaded: selected GameRelease, selected game directory, and Advanced Mode.
_Avoid_: Game selection, current game state.

**Entry**:
The human-readable label stored for a FormID, usually an EditorID or record name.
_Avoid_: Name value.

**Entry Extraction**:
The part of Plugin Ingestion that chooses the Entry stored for a FormID, preferring EditorID, then Mutagen record names, then a deterministic fallback label.
_Avoid_: Name helper, record naming.

**FormID text file**:
A pipe-delimited import file whose rows provide Plugin, FormID, and Entry values without reading binary plugins.
_Avoid_: List file, text import.

**FormID Record Store**:
The persisted collection of FormID records for a single GameRelease, regardless of whether records came from plugin ingestion or a FormID text file.
_Avoid_: Database writer, write path.

**Update Mode**:
An ingestion mode that replaces existing FormID records for each successfully ingested Plugin before storing the new records for that Plugin. Skipped Plugins do not delete existing records; Plugin matching is case-insensitive, while stored Plugin values preserve their source casing.
_Avoid_: Full database refresh, exact-case replacement.

**Processing Run**:
A single execution that turns either selected Plugins or one FormID text file into records in a FormID Record Store for one GameRelease. A Processing Run is scoped by its Update Mode and ends as completed, completed with warnings, completed with failures, cancelled, or failed.
_Avoid_: Processing job, import task, plugin processing.

**Processing Warning**:
A user-visible condition from a Processing Run that did not stop ingestion, such as a Skipped Plugin or recoverable record issue. Processing Warnings can make a Processing Run complete with warnings but do not count as failed.
_Avoid_: Non-fatal error, ignored error.

**User Workflow**:
The end-to-end user interaction that turns a selected GameRelease, a game directory or FormID text file, Plugin selections, database path, and Update Mode into one FormID processing run.
_Avoid_: UI event flow, MainWindow logic.
