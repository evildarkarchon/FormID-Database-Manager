# FormID Database Manager Context

This context covers building a searchable database of FormID records from Bethesda game plugins and FormID text files.

## Language

**FormID**:
A hexadecimal identifier for a record inside a Bethesda game plugin. A FormID is stored together with the Plugin it came from and a human-readable Entry.

**Plugin**:
A Bethesda game data file selected for processing, such as an `.esm`, `.esp`, or `.esl` file.
_Avoid_: Mod, except when referring to Mutagen type names.

**GameRelease**:
The target Bethesda game or edition that determines plugin layout, base plugins, and database table selection.
_Avoid_: Game type, release enum.

**Entry**:
The human-readable label stored for a FormID, usually an EditorID or record name.
_Avoid_: Name value.

**FormID text file**:
A pipe-delimited import file whose rows provide Plugin, FormID, and Entry values without reading binary plugins.
_Avoid_: List file, text import.

**FormID Record Store**:
The persisted collection of FormID records for a single GameRelease, regardless of whether records came from plugin ingestion or a FormID text file.
_Avoid_: Database writer, write path.
