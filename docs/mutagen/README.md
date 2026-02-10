# Mutagen Library Documentation

Mutagen is a C# library for reading, creating, and manipulating Bethesda game plugin files (.esp, .esm, .esl). It provides strongly-typed record definitions, lazy binary parsing via overlays, a unified FormKey/FormLink system for cross-mod record references, and load order/link cache infrastructure for resolving records across multiple plugins.

This documentation covers Mutagen version **0.51.5** as referenced by the FormID Database Manager project.

---

## Project Index

| Project | Description | Scope |
|---------|-------------|-------|
| [Mutagen.Bethesda.Kernel](Mutagen.Bethesda.Kernel.md) | Minimal foundational types: `GameRelease`, `GameCategory`, `ModKey`, `ModPath`, `ModType`, `MasterStyle` | Kernel types, zero game-specific dependencies |
| [Mutagen.Bethesda.Core](Mutagen.Bethesda.Core.md) | Core infrastructure: `FormKey`, `FormLink`, `ILinkCache`, `LoadOrder`, `IMod`, `IMajorRecord`, environments, archives, strings, Papyrus (PEX) parsing | Core infrastructure for all games |
| [Mutagen.Bethesda](Mutagen.Bethesda.md) | Umbrella NuGet package aggregating all game-specific projects | Convenience meta-package |
| [Mutagen.Bethesda.Skyrim](Mutagen.Bethesda.Skyrim.md) | Skyrim record types (LE, SE, GOG, VR, Enderal) -- 120+ record types with full catalog | Skyrim game-specific records |
| [Mutagen.Bethesda.Starfield](Mutagen.Bethesda.Starfield.md) | Starfield record types -- 190+ record types including planets, OMOD system, snap templates | Starfield game-specific records |
| [Mutagen.Bethesda.Fallout4](Mutagen.Bethesda.Fallout4.md) | Fallout 4 record types (FO4, FO4VR) -- 125+ record types with OMOD, holotapes, settlements | Fallout 4 game-specific records |
| [Mutagen.Bethesda.Oblivion](Mutagen.Bethesda.Oblivion.md) | Oblivion record types (Oblivion, Oblivion Remastered) -- 54 record types with creatures, scripts, pathgrids | Oblivion game-specific records |
| [Mutagen.Bethesda.Json](Mutagen.Bethesda.Json.md) | Newtonsoft.Json converters for `ModKey`, `FormKey`, and `FormLink` types | JSON serialization |
| [Mutagen.Bethesda.Sqlite](Mutagen.Bethesda.Sqlite.md) | SQLite-backed FormKey allocator for deterministic, persistent FormID assignment | Persistent FormID allocation |
| [Mutagen.Bethesda.Autofac](Mutagen.Bethesda.Autofac.md) | Autofac DI module registering all core Mutagen services | Dependency injection |
| [Mutagen.Bethesda.SourceGenerators](Mutagen.Bethesda.SourceGenerators.md) | Roslyn source generator for custom aspect interface wrappers | Build-time code generation |
| [Mutagen.Bethesda.WPF](Mutagen.Bethesda.WPF.md) | WPF controls for FormKey/ModKey picking, load order views, and reflection-based settings UI | WPF UI components (Windows only) |

---

## Architecture Diagram

```
                        +----------------------------+
                        |  Mutagen.Bethesda (meta)   |
                        |   (no code, aggregates all)|
                        +----------------------------+
                                     |
              +----------------------+----------------------+
              |                      |                      |
   +----------v--------+  +---------v---------+  +---------v---------+
   | Mutagen.Bethesda.  |  | Mutagen.Bethesda. |  | Mutagen.Bethesda. |  ...
   |      Skyrim        |  |    Fallout4       |  |    Starfield      |
   | (SkyrimMod, Npc,   |  | (Fallout4Mod,     |  | (StarfieldMod,    |
   |  Weapon, Cell, ...) |  |  Npc, Holotape,..) |  |  Planet, OMOD,..) |
   +----------+---------+  +---------+---------+  +---------+---------+
              |                      |                      |
              +----------------------+----------------------+
                                     |
                        +------------v-----------+
                        |  Mutagen.Bethesda.Core |
                        |  (FormKey, ILinkCache, |
                        |   LoadOrder, IMod,     |
                        |   Archives, Env, PEX)  |
                        +------------+-----------+
                                     |
                        +------------v-----------+
                        | Mutagen.Bethesda.Kernel|
                        |  (GameRelease, ModKey, |
                        |   ModPath, ModType)    |
                        +------------------------+

   Sibling/Utility Projects:
   +---------------------+  +---------------------+  +---------------------+
   | Mutagen.Bethesda.   |  | Mutagen.Bethesda.   |  | Mutagen.Bethesda.   |
   |        Json         |  |       Sqlite        |  |      Autofac        |
   | (JSON converters)   |  | (FormKey allocator) |  | (DI module)         |
   +---------------------+  +---------------------+  +---------------------+

   +---------------------+  +---------------------+
   | Mutagen.Bethesda.   |  | Mutagen.Bethesda.   |
   |  SourceGenerators   |  |        WPF          |
   | (Roslyn codegen)    |  | (WPF UI controls)   |
   +---------------------+  +---------------------+
```

The dependency flow is bottom-up: **Kernel** has no Mutagen dependencies, **Core** depends on Kernel, and each **game-specific project** depends on Core. The utility projects (Json, Sqlite, Autofac, WPF) depend on Core and/or Kernel as needed.

---

## Common Patterns

These cross-cutting patterns appear throughout the Mutagen codebase.

### Getter/Setter Interface Pattern

Every record type defines a read-only **getter** interface and a mutable **setter** interface:

```csharp
// Read-only -- used by binary overlays and when you only need to inspect data
public interface INpcGetter : ISkyrimMajorRecordGetter, INamedGetter { ... }

// Mutable -- used when you need to modify data
public interface INpc : INpcGetter, ISkyrimMajorRecord, INamed { ... }
```

This pattern applies at every level:
- `IModGetter` / `IMod`
- `IMajorRecordGetter` / `IMajorRecord`
- `IGroupGetter<T>` / `IGroup<T>`
- `IFormLinkGetter<T>` / `IFormLink<T>`
- Aspect interfaces: `INamedGetter` / `INamed`, `IKeywordedGetter<T>` / `IKeyworded<T>`

The getter interfaces are covariant (use `out T`) where possible, enabling safe upcasting.

### Binary Overlay Pattern (Lazy Parsing)

Each record type has a `BinaryOverlay` class that reads data directly from raw bytes without full deserialization. Created via `CreateFromBinaryOverlay`, the overlay only parses fields when they are accessed.

```csharp
// Lazy read -- only parses fields on access, minimal memory
using var mod = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
    .FromPath(path)
    .Readonly();   // Returns IModDisposeGetter (overlay, must dispose)

// Full deserialization -- all fields parsed upfront, mutable
var mod = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
    .FromPath(path)
    .Mutable();    // Returns ISkyrimMod (mutable)
```

Overlays implement `IModDisposeGetter` (which extends `IDisposable`) and must be disposed when no longer needed.

### FormKey and FormLink System

**FormKey** is a `ModKey` + record ID pair that uniquely identifies a record without load-order ambiguity:

```csharp
// FormKey: "012ABC:Skyrim.esm" -- always resolves to the same record
FormKey key = FormKey.Factory("012ABC:Skyrim.esm");
```

**FormLink\<T\>** adds type safety by associating a FormKey with a specific record type:

```csharp
// Type-safe reference to a Weapon record
FormLink<IWeaponGetter> weaponLink = new FormLink<IWeaponGetter>(formKey);

// Resolve against a link cache
if (weaponLink.TryResolve(linkCache, out var weapon))
{
    Console.WriteLine(weapon.Name);
}
```

FormLinks are used throughout record definitions to reference other records (e.g., an NPC's race, a weapon's enchantment).

### ModKey Identification

**ModKey** identifies a plugin file by name and type (`.esm`, `.esp`, `.esl`):

```csharp
ModKey skyrim = ModKey.FromNameAndExtension("Skyrim.esm");
ModKey myMod = "MyMod.esp";  // implicit conversion

// Properties
skyrim.Name     // "Skyrim"
skyrim.Type     // ModType.Master
skyrim.FileName // "Skyrim.esm"
```

Name comparison is case-insensitive. Hash codes are cached at construction time.

### LoadOrder and LinkCache

**LoadOrder** represents the ordered list of plugins as they would be loaded by the game engine:

```csharp
// Get listings from game installation
var listings = LoadOrder.GetListings(GameRelease.SkyrimSE, dataPath);
```

**ILinkCache** resolves FormKeys and FormLinks to actual records across the entire load order, respecting override priority:

```csharp
// Generic resolution (preferred -- type-safe and faster)
if (linkCache.TryResolve<INpcGetter>(formKey, out var npc))
{
    Console.WriteLine(npc.EditorID);
}

// Resolve a FormLink directly
if (npcRaceLink.TryResolve(linkCache, out var race))
{
    Console.WriteLine(race.Name);
}
```

`ResolveTarget.Winner` (default) returns the last override; `ResolveTarget.Origin` returns the original definition.

### GameRelease Enum and Multi-Game Support

The `GameRelease` enum covers all supported game editions:

| GameRelease | GameCategory |
|-------------|-------------|
| `Oblivion`, `OblivionRE` | `Oblivion` |
| `SkyrimLE`, `SkyrimSE`, `SkyrimSEGog`, `SkyrimVR`, `EnderalLE`, `EnderalSE` | `Skyrim` |
| `Fallout4`, `Fallout4VR` | `Fallout4` |
| `Starfield` | `Starfield` |

Game-agnostic code uses `GameRelease` to select the correct binary format, constants, and record types at runtime. The non-generic `ModFactory` class creates the appropriate mod type from a `GameRelease`:

```csharp
using var mod = ModFactory.ImportGetter(modPath, GameRelease.SkyrimSE);
```

---

## Quick Reference

### Opening a Mod (Read-Only Overlay)

```csharp
using var mod = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
    .FromPath(pluginPath)
    .Readonly();
```

### Opening a Mod (Mutable)

```csharp
var mod = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
    .FromPath(pluginPath)
    .Mutable();
```

### Opening a Mod (Game-Agnostic)

```csharp
using var mod = ModFactory.ImportGetter(modPath, GameRelease.SkyrimSE);
```

### Iterating All Records of a Type

```csharp
foreach (var weapon in mod.Weapons.Records)
{
    Console.WriteLine($"{weapon.FormKey}: {weapon.EditorID} - {weapon.Name}");
}
```

### Iterating All Major Records

```csharp
foreach (var record in mod.EnumerateMajorRecords())
{
    Console.WriteLine($"{record.FormKey}: {record.EditorID}");
}
```

### Resolving a FormLink

```csharp
var linkCache = mod.ToImmutableLinkCache();

FormLink<INpcGetter> npcLink = ...;
if (npcLink.TryResolve(linkCache, out var npc))
{
    Console.WriteLine(npc.Name);
}
```

### Creating a New Mod and Writing

```csharp
var newMod = new SkyrimMod(ModKey.FromNameAndExtension("MyPatch.esp"), SkyrimRelease.SkyrimSE);

// Add records, modify data...

newMod.BeginWrite
    .ToPath(outputPath)
    .Write();
```

### Using GameEnvironment

```csharp
using var env = GameEnvironment.Typical.Construct<ISkyrimModGetter>(GameRelease.SkyrimSE);

// Access the full load order and link cache
foreach (var listing in env.LoadOrder)
{
    Console.WriteLine(listing.ModKey);
}

// Resolve any record across the entire load order
if (env.LinkCache.TryResolve<INpcGetter>(someFormKey, out var npc))
{
    Console.WriteLine(npc.Name);
}
```

### Detecting Game Installations

```csharp
if (GameLocations.TryGetGameFolder(GameRelease.SkyrimSE, out var gamePath))
{
    Console.WriteLine($"Skyrim SE found at: {gamePath}");
}
```

---

## Game-Specific Differences

| Feature | Oblivion | Skyrim | Fallout 4 | Starfield |
|---------|----------|--------|-----------|-----------|
| Small masters (ESL) | No | Yes (SE+) | Yes | Yes |
| Medium masters | No | No | No | Yes |
| Keywords | No | Yes | Yes | Yes |
| Papyrus scripts | No | Yes | Yes | Yes |
| Object modifications (OMOD) | No | No | Yes | Yes |
| Creatures separate from NPCs | Yes | No | No | No |
| PathGrids (vs NavMesh) | Yes | No | No | No |
| Roads in worldspaces | Yes | No | No | No |
| Dialog topics under quests | No | No | Yes | Yes |
| Overridden forms list | No | Yes | Yes | Yes |
| Localization support | No | Yes | Yes | Yes |
| Record count | ~54 types | ~120 types | ~125 types | ~190 types |
