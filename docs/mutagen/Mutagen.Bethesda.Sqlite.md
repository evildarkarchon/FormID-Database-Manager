# Mutagen.Bethesda.Sqlite

Provides a SQLite-backed FormKey allocator for persistent, deterministic FormID allocation across mod-building sessions. This ensures that EditorIDs are consistently mapped to the same FormIDs, even when a mod is rebuilt.

**Namespace:** `Mutagen.Bethesda.Plugins.Allocators`

**Dependencies:** `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`

---

## Namespace: Mutagen.Bethesda.Plugins.Allocators

### Class: SQLiteFormKeyAllocator

A FormKey allocator backed by a SQLite database. It persists the mapping between EditorIDs and FormIDs, allowing patchers to produce stable, reproducible output. Multiple patchers can share the same database, with each patcher tracked separately.

```csharp
public sealed class SQLiteFormKeyAllocator : BaseSharedFormKeyAllocator
```

**Inherits:** `BaseSharedFormKeyAllocator` (from Mutagen.Bethesda.Core)

#### Static Fields

| Field | Type | Description |
|-------|------|-------------|
| `DefaultPatcherName` | `static readonly string` | Default patcher name used when no explicit name is provided. Value: `"default"`. |

#### Constructors

```csharp
// Single-patcher mode: uses DefaultPatcherName
public SQLiteFormKeyAllocator(IMod mod, string dbPath)

// Multi-patcher mode: specifies which patcher is active
public SQLiteFormKeyAllocator(IMod mod, string dbPath, string activePatcherName)
```

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `mod` | `IMod` | The mod being built. Used to track `NextFormID` and the mod's `ModKey`. |
| `dbPath` | `string` | Filesystem path to the SQLite database file. Created automatically if it does not exist. |
| `activePatcherName` | `string` | A unique name identifying the current patcher. Used to track which patcher allocated each FormID. |

#### Public Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetNextFormKey` | `public override FormKey GetNextFormKey()` | Allocates the next available FormID that is not already recorded in the database. Thread-safe (locks on both the DB connection and the mod). Throws `OverflowException` if all FormIDs (up to `0xFFFFFF`) are exhausted. |
| `Commit` | `public override void Commit()` | Persists all pending FormID allocations to the SQLite database. Must be called to save new mappings. |
| `Rollback` | `public override void Rollback()` | Reverts all uncommitted allocations. Resets `Mod.NextFormID` to its initial value and re-opens the database connection. |
| `ClearPatcher` | `public void ClearPatcher()` | Removes all FormID records allocated by the current patcher. |
| `ClearPatcher` | `public void ClearPatcher(string patcherName)` | Removes all FormID records allocated by the specified patcher. |
| `IsPathOfAllocatorType` | `public static bool IsPathOfAllocatorType(string path)` | Checks whether a file at the given path is a SQLite database by reading its 16-byte header. Returns `false` if the file does not exist or cannot be read. |

#### Protected Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetNextFormKeyNotNull` | `protected override FormKey GetNextFormKeyNotNull(string editorID)` | Allocates a FormKey for a given EditorID. If the EditorID was previously allocated, returns the same FormKey. If it was allocated by a different patcher (in multi-patcher mode), throws `ConstraintException`. Thread-safe. |
| `Dispose` | `protected override void Dispose(bool disposing)` | Disposes the internal database connection. |

---

## Database Schema

The allocator creates two tables in its SQLite database:

### Table: FormIDs

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `FormID` | `uint` | Primary Key | The allocated FormID value. |
| `EditorID` | `string` | Alternate Key (unique) | The EditorID associated with this FormID. |
| `PatcherID` | `uint` | Foreign Key -> Patchers.PatcherID | The patcher that allocated this FormID. |

### Table: Patchers

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `PatcherID` | `uint` | Primary Key (auto-increment) | Unique identifier for the patcher. |
| `PatcherName` | `string` | Alternate Key (unique) | Human-readable patcher name. |

---

## Internal Types

These types are internal to the implementation but documented for completeness:

### Record: SQLiteFormKeyAllocatorFormIDRecord

```csharp
record SQLiteFormKeyAllocatorFormIDRecord(string EditorID, uint FormID, uint PatcherID);
```

EF Core entity mapping to the `FormIDs` table.

### Record: SQLiteFormKeyAllocatorPatcherRecord

```csharp
record SQLiteFormKeyAllocatorPatcherRecord(uint PatcherID, string PatcherName);
```

EF Core entity mapping to the `Patchers` table.

### Class: SQLiteFormKeyAllocatorDbContext (internal)

An internal `DbContext` subclass that manages the SQLite connection and EF Core model.

---

## Thread Safety

All public methods that access the database or mod state use `lock` statements for thread safety. The locking order is:
1. `_connection` (database lock)
2. `Mod` (mod state lock, when needed)

---

## Usage Example

```csharp
using Mutagen.Bethesda.Plugins.Allocators;

// Create or open a SQLite-backed allocator
using var allocator = new SQLiteFormKeyAllocator(myMod, "allocations.sqlite", "MyPatcher");

// Allocate a FormKey for an EditorID (deterministic across runs)
FormKey key = allocator.GetNextFormKey("MyWeapon");

// ... add records to the mod using the allocated FormKey ...

// Persist the allocations
allocator.Commit();
```
