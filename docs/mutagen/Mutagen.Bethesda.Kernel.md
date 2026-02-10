# Mutagen.Bethesda.Kernel

A lightweight C# library containing the minimal foundational types for working with Bethesda game plugin structures. This package is designed as a low-dependency import providing basic definitions that other Mutagen packages build upon.

**Dependencies:** `Noggog.CSharpExt`

**Target Frameworks:** `net8.0`, `net9.0`, `net10.0`

---

## Namespace: Mutagen.Bethesda

### Enum: GameRelease

Enumerates all specific Bethesda game releases supported by Mutagen. Each member has a `[Description]` attribute with a human-readable name.

```csharp
public enum GameRelease
{
    [Description("Oblivion")]
    Oblivion = 0,

    [Description("Skyrim Legendary Edition")]
    SkyrimLE = 1,

    [Description("Skyrim Special Edition")]
    SkyrimSE = 2,

    [Description("Skyrim VR")]
    SkyrimVR = 3,

    [Description("Fallout 4")]
    Fallout4 = 4,

    [Description("Enderal LE")]
    EnderalLE = 5,

    [Description("Enderal SE")]
    EnderalSE = 6,

    [Description("Skyrim Special Edition GOG")]
    SkyrimSEGog = 7,

    [Description("Starfield")]
    Starfield = 8,

    [Description("Fallout 4 VR")]
    Fallout4VR = 9,

    [Description("Oblivion Remastered")]
    OblivionRE = 10,
}
```

---

### Enum: GameCategory

Groups game releases into broader categories that share similar or identical plugin formats.

```csharp
public enum GameCategory
{
    [Description("Oblivion")]
    Oblivion,

    [Description("Skyrim")]
    Skyrim,

    [Description("Fallout4")]
    Fallout4,

    [Description("Starfield")]
    Starfield,
}
```

---

### Static Class: GameReleaseKernelExt

Extension methods for converting between `GameRelease` and `GameCategory`, plus querying flag positions.

```csharp
public static class GameReleaseKernelExt
```

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `ToCategory` | `public static GameCategory ToCategory(this GameRelease release)` | Converts a specific `GameRelease` to its broader `GameCategory`. Maps Oblivion/OblivionRE to Oblivion, all Skyrim/Enderal variants to Skyrim, Fallout4/Fallout4VR to Fallout4, and Starfield to Starfield. |
| `GetMasterFlagIndex` | `public static int GetMasterFlagIndex(this GameCategory release)` | Returns the bit index for the master flag in plugin headers. Always returns `0x0000_0001`. |
| `GetLocalizedFlagIndex` | `public static int? GetLocalizedFlagIndex(this GameCategory release)` | Returns the bit index for the localized flag in plugin headers. Returns `0x0000_0080`. |

#### GameRelease to GameCategory Mapping

| GameRelease | GameCategory |
|-------------|-------------|
| `Oblivion`, `OblivionRE` | `Oblivion` |
| `SkyrimLE`, `SkyrimSE`, `SkyrimSEGog`, `SkyrimVR`, `EnderalLE`, `EnderalSE` | `Skyrim` |
| `Fallout4`, `Fallout4VR` | `Fallout4` |
| `Starfield` | `Starfield` |

---

## Namespace: Mutagen.Bethesda.Kernel

### Static Class: Constants

File extension string constants for Bethesda plugin types.

```csharp
public static class Constants
{
    public const string Esm = "esm";
    public const string Esp = "esp";
    public const string Esl = "esl";
}
```

---

## Namespace: Mutagen.Bethesda.Plugins

### Enum: ModType

Classifies plugin files by their extension type.

```csharp
public enum ModType
{
    Master,   // .esm
    Light,    // .esl
    Plugin,   // .esp
}
```

---

### Static Class: ModTypeExt

Extension methods for `ModType`.

```csharp
public static class ModTypeExt
```

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `GetFileExtension` | `public static string GetFileExtension(this ModType modType)` | Returns the file extension string (`"esm"`, `"esl"`, or `"esp"`) corresponding to the `ModType`. |

---

### Enum: MasterStyle

Describes the master file addressing style used by a plugin, which determines the size of the FormID address space available.

```csharp
public enum MasterStyle
{
    Full,    // Standard full-size master
    Small,   // Small/light master (ESL)
    Medium,  // Medium master (Starfield)
}
```

---

### Interface: IModKeyed

Indicates that an object is associated with a `ModKey`.

```csharp
public interface IModKeyed
{
    /// <summary>
    /// The associated ModKey
    /// </summary>
    ModKey ModKey { get; }
}
```

---

### Struct: ModKey

A unique identifier for a mod, consisting of a name and a type (master/plugin/light). This is one of the most fundamental types in Mutagen, used to identify mods throughout the entire library.

**Format:** `[ModName].[esp/esm/esl]` (e.g., `Skyrim.esm`, `MyMod.esp`)

**Implements:** `IEquatable<ModKey>`, `IModKeyed`

**Attributes:** `[DebuggerDisplay("ModKey {FileName}")]`

```csharp
public readonly struct ModKey : IEquatable<ModKey>, IModKeyed
```

#### Fields and Properties

| Member | Type | Description |
|--------|------|-------------|
| `Null` | `static readonly ModKey` | Singleton representing a null/empty ModKey. |
| `NullStr` | `const string` | String representation of null: `"Null"`. |
| `Name` | `string` | The mod name (without extension). Returns `string.Empty` if null. |
| `Type` | `ModType` | The mod type (Master, Plugin, or Light). |
| `FileName` | `FileName` | The full filename (e.g., `"Skyrim.esm"`). Computed property. |
| `IsNull` | `bool` | Returns `true` if the name is null or whitespace. |

#### Static Factory Methods

**TryFromNameAndExtension** - Parse from a full filename string (e.g., `"MyMod.esp"`):

```csharp
// Full overload with error reason
public static bool TryFromNameAndExtension(ReadOnlySpan<char> str, out ModKey modKey, out string errorReason)

// String overload with error reason
public static bool TryFromNameAndExtension(string? str, out ModKey modKey, out string errorReason)

// Span overload without error reason
public static bool TryFromNameAndExtension(ReadOnlySpan<char> str, out ModKey modKey)

// String overload without error reason
public static bool TryFromNameAndExtension(string? str, out ModKey modKey)

// Nullable return variants
public static ModKey? TryFromNameAndExtension(ReadOnlySpan<char> str)
public static ModKey? TryFromNameAndExtension(string? str)
```

**TryFromFileName** - Parse from a `FileName` value:

```csharp
public static bool TryFromFileName(FileName? fileName, out ModKey modKey, out string errorReason)
public static bool TryFromFileName(FileName? fileName, out ModKey modKey)
public static ModKey? TryFromFileName(FileName? fileName)
```

**TryFromName** - Parse from a name string with an explicit `ModType`:

```csharp
public static bool TryFromName(ReadOnlySpan<char> str, ModType type, out ModKey modKey, out string errorReason)
public static bool TryFromName(string? str, ModType type, out ModKey modKey, out string errorReason)
public static ModKey? TryFromName(ReadOnlySpan<char> str, ModType type)
public static ModKey? TryFromName(string? str, ModType type)
public static bool TryFromName(ReadOnlySpan<char> str, ModType type, out ModKey modKey)
public static bool TryFromName(string? str, ModType type, out ModKey modKey)
```

**FromNameAndExtension** - Parse or throw:

```csharp
// Throws ArgumentException if string is malformed
public static ModKey FromNameAndExtension(ReadOnlySpan<char> str)
```

**FromFileName** - Parse from a FileName or throw:

```csharp
// Throws ArgumentException if FileName is malformed
public static ModKey FromFileName(FileName fileName)
```

**FromName** - Parse from a name and type or throw:

```csharp
// Throws ArgumentException if name is invalid
public static ModKey FromName(ReadOnlySpan<char> str, ModType type)
```

#### Utility Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `HasInvalidCharacters` | `public static bool HasInvalidCharacters(ReadOnlySpan<char> str)` | Checks if a string contains characters invalid for mod names (path separators, control characters, etc.). |
| `TryConvertExtensionToType` | `public static bool TryConvertExtensionToType(ReadOnlySpan<char> str, out ModType modType)` | Converts a file extension string (`"esm"`, `"esp"`, `"esl"`) to a `ModType` enum. Case-insensitive. |

#### Equality and Comparison

- Name comparison is **case-insensitive**
- Two null ModKeys are considered equal
- Hash code is cached at construction time for performance (ModKeys are created rarely but hashed often)

#### Operators

```csharp
public static bool operator ==(ModKey? a, ModKey? b)
public static bool operator !=(ModKey? a, ModKey? b)
```

#### Implicit Conversions

```csharp
public static implicit operator ModKey(string nameAndExtension)   // "MyMod.esp" -> ModKey
public static implicit operator ModKey(FileName fileName)          // FileName -> ModKey
public static implicit operator FileName(ModKey modKey)            // ModKey -> FileName
```

#### Built-in Comparers

| Comparer | Type | Description |
|----------|------|-------------|
| `AlphabeticalAndMastersFirst` | `Comparer<ModKey>` | Sorts by mod type first (masters before plugins), then alphabetically by name. |
| `ByTypeComparer` | `Comparer<ModKey>` | Sorts only by `ModType`. |
| `Alphabetical` | `Comparer<ModKey>` | Sorts alphabetically by full filename. |
| `LoadOrderComparer(...)` | `Comparer<ModKey>` | Creates a comparer based on a provided load order list. Throws `ArgumentOutOfRangeException` if a ModKey is not found in the load order (unless a fallback comparer is provided). |

```csharp
public static Comparer<ModKey> LoadOrderComparer(
    IReadOnlyList<ModKey> loadOrder,
    Comparer<ModKey>? matchingFallback = null)
```

---

### Record: ModPath

Pairs a `ModKey` with a filesystem path. This is a sealed record type that implements `IModKeyed`.

```csharp
public sealed record ModPath : IModKeyed
```

#### Fields and Properties

| Member | Type | Description |
|--------|------|-------------|
| `Empty` | `static readonly ModPath` | Singleton representing an empty ModPath (null key, empty path). |
| `ModKey` | `ModKey` | The mod identifier. |
| `Path` | `FilePath` | The filesystem path to the mod file. |

#### Constructors

```csharp
// Explicit ModKey + path
public ModPath(ModKey modKey, FilePath path)

// Parse ModKey from the file path's filename
public ModPath(FilePath path)

// Parse ModKey from the string path's filename
public ModPath(string path)
```

#### Static Factory Methods

```csharp
public static ModPath FromPath(FilePath path)
public static bool TryFromPath(FilePath path, out ModPath modPath)
```

#### Implicit Conversions

```csharp
public static implicit operator ModPath(string str)        // string -> ModPath
public static implicit operator ModPath(FilePath filePath)  // FilePath -> ModPath
public static implicit operator string(ModPath p)           // ModPath -> string (path)
public static implicit operator FilePath(ModPath p)         // ModPath -> FilePath
public static implicit operator ModKey(ModPath p)           // ModPath -> ModKey
```

#### ToString

Returns `"ModKey => Path"` when the path is not empty, or just the ModKey string representation otherwise.

---

## Usage Examples

### Creating ModKeys

```csharp
// From a filename string
ModKey skyrim = ModKey.FromNameAndExtension("Skyrim.esm");

// Using implicit conversion
ModKey myMod = "MyMod.esp";

// Using TryFromNameAndExtension
if (ModKey.TryFromNameAndExtension("CustomMod.esl", out var modKey))
{
    Console.WriteLine($"Parsed: {modKey.Name}, Type: {modKey.Type}");
}

// From name and explicit type
ModKey master = ModKey.FromName("DLC01", ModType.Master);
```

### Working with GameRelease

```csharp
GameRelease release = GameRelease.SkyrimSE;
GameCategory category = release.ToCategory(); // GameCategory.Skyrim
```

### ModPath Usage

```csharp
// From a file path
var modPath = new ModPath(@"C:\Games\Skyrim\Data\Skyrim.esm");
Console.WriteLine(modPath.ModKey.Name);  // "Skyrim"
Console.WriteLine(modPath.Path);         // "C:\Games\Skyrim\Data\Skyrim.esm"
```
