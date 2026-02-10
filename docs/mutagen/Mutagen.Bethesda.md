# Mutagen.Bethesda

An umbrella NuGet package that aggregates all game-specific Mutagen projects into a single dependency. This project contains no source code of its own -- it exists solely as a convenience package that references all game-specific libraries.

**Dependencies:**
- `Mutagen.Bethesda.Core`
- `Mutagen.Bethesda.Skyrim`
- `Mutagen.Bethesda.Fallout4`
- `Mutagen.Bethesda.Oblivion`
- `Mutagen.Bethesda.Starfield`
- `Loqui`
- `Noggog.CSharpExt`

**Target Frameworks:** `net8.0`, `net9.0`, `net10.0`

**Description:** A C# library for manipulating, creating, and analyzing Bethesda mods.

---

## Purpose

`Mutagen.Bethesda` is the recommended top-level package for applications that need to work with multiple Bethesda game formats. By depending on this single package, consumers automatically get access to:

- **Mutagen.Bethesda.Core** -- Core services, plugin parsing infrastructure, environment setup, load order management
- **Mutagen.Bethesda.Skyrim** -- Skyrim LE/SE/VR/GOG record types and processing
- **Mutagen.Bethesda.Fallout4** -- Fallout 4/VR record types and processing
- **Mutagen.Bethesda.Oblivion** -- Oblivion/Oblivion Remastered record types and processing
- **Mutagen.Bethesda.Starfield** -- Starfield record types and processing

## When to Use

| Scenario | Recommended Package |
|----------|-------------------|
| Working with plugins from multiple games | `Mutagen.Bethesda` |
| Working with a single game only | `Mutagen.Bethesda.Skyrim` (or the specific game package) |
| Only need basic types (ModKey, FormKey, GameRelease) | `Mutagen.Bethesda.Kernel` |
| Need core infrastructure without specific game records | `Mutagen.Bethesda.Core` |

## No Public API

This project does not define any public types, interfaces, or classes of its own. All public API surface comes from the transitive dependencies listed above. See the documentation for each individual project for their respective APIs.
