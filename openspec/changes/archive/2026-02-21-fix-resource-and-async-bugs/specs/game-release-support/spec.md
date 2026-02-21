## ADDED Requirements

### Requirement: Supported game releases are defined in a single location
The system SHALL maintain a single authoritative mapping from `GameRelease` enum values to SQLite table name strings in `GameReleaseHelper.GetSafeTableName`. All services that require a table name SHALL call this method rather than maintaining their own copy. `OblivionRE` SHALL NOT be included in the supported set.

#### Scenario: Supported release returns correct table name
- **WHEN** `GameReleaseHelper.GetSafeTableName` is called with a supported `GameRelease`
- **THEN** it returns the expected string (e.g., `SkyrimLE` → `"SkyrimLE"`, `EnderalSE` → `"EnderalSE"`)

#### Scenario: Unsupported release throws
- **WHEN** `GameReleaseHelper.GetSafeTableName` is called with an unsupported `GameRelease` (e.g., `OblivionRE`)
- **THEN** it throws `ArgumentException` with a message identifying the unsupported release

#### Scenario: All services use the same table name
- **WHEN** `DatabaseService`, `ModProcessor`, and `FormIdTextProcessor.BatchInserter` each resolve a table name for the same `GameRelease`
- **THEN** they all return the same string (i.e., there is no divergence between components)

---

### Requirement: Game data path is resolved in a single location
The system SHALL provide a single helper `GameReleaseHelper.ResolveDataPath(string gameDirectory)` that normalises a user-supplied directory to the `Data` subfolder path. All services that need the data path SHALL call this helper.

#### Scenario: Data directory passed directly
- **WHEN** `ResolveDataPath` is called with a path whose final segment is `"Data"` (case-insensitive)
- **THEN** it returns that path unchanged

#### Scenario: Game root directory passed
- **WHEN** `ResolveDataPath` is called with a path whose final segment is not `"Data"`
- **THEN** it returns `Path.Combine(gameDirectory, "Data")`

---

### Requirement: Mutagen binary overlay covers all supported game releases
The `CreateFromBinaryOverlay` switch in `ModProcessor` SHALL handle every `GameRelease` that `GetSafeTableName` supports: `Oblivion`, `SkyrimLE`, `SkyrimSE`, `SkyrimSEGog`, `SkyrimVR`, `EnderalLE`, `EnderalSE`, `Fallout4`, `Fallout4VR`, `Starfield`. Unsupported releases SHALL throw `NotSupportedException`.

#### Scenario: SkyrimLE plugin is opened
- **WHEN** `ModProcessor.ProcessPlugin` is called with `GameRelease.SkyrimLE`
- **THEN** it calls `SkyrimMod.CreateFromBinaryOverlay` with `SkyrimRelease.SkyrimLE` without throwing

#### Scenario: EnderalSE plugin is opened
- **WHEN** `ModProcessor.ProcessPlugin` is called with `GameRelease.EnderalSE`
- **THEN** it calls `SkyrimMod.CreateFromBinaryOverlay` with `SkyrimRelease.EnderalSE` without throwing

#### Scenario: Fallout4VR plugin is opened
- **WHEN** `ModProcessor.ProcessPlugin` is called with `GameRelease.Fallout4VR`
- **THEN** it calls `Fallout4Mod.CreateFromBinaryOverlay` with `Fallout4Release.Fallout4VR` without throwing

#### Scenario: Unsupported release throws during plugin open
- **WHEN** `ModProcessor.ProcessPlugin` is called with an unsupported `GameRelease`
- **THEN** it throws `NotSupportedException`

---

### Requirement: Game detection distinguishes SkyrimLE from SkyrimSE and detects Enderal
`GameDetectionService.DetectGame` SHALL correctly identify `SkyrimLE`, `EnderalLE`, and `EnderalSE` from directory structure. Detection SHALL check for the Enderal master file before checking SE/LE executables.

#### Scenario: SkyrimSE detected
- **WHEN** the directory contains `Skyrim.esm` and the game root contains `SkyrimSE.exe` but NOT `Enderal - Forgotten Stories.esm`
- **THEN** `DetectGame` returns `GameRelease.SkyrimSE`

#### Scenario: SkyrimLE detected
- **WHEN** the directory contains `Skyrim.esm` and the game root does NOT contain `SkyrimSE.exe` and does NOT contain `Enderal - Forgotten Stories.esm`
- **THEN** `DetectGame` returns `GameRelease.SkyrimLE`

#### Scenario: EnderalSE detected
- **WHEN** the directory contains `Skyrim.esm` AND `Enderal - Forgotten Stories.esm`, and the game root contains `SkyrimSE.exe`
- **THEN** `DetectGame` returns `GameRelease.EnderalSE`

#### Scenario: EnderalLE detected
- **WHEN** the directory contains `Skyrim.esm` AND `Enderal - Forgotten Stories.esm`, and the game root contains `TESV.exe` but NOT `SkyrimSE.exe`
- **THEN** `DetectGame` returns `GameRelease.EnderalLE`
