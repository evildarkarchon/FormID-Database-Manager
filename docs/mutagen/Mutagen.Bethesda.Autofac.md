# Mutagen.Bethesda.Autofac

Provides an Autofac dependency injection module that registers all core Mutagen services, making it easy to integrate Mutagen into applications that use the Autofac IoC container.

**Namespace:** `Mutagen.Bethesda.Autofac`

**Dependencies:** `Autofac`, `Noggog.Autofac`, `Mutagen.Bethesda.Core` (and its transitive dependencies)

---

## Namespace: Mutagen.Bethesda.Autofac

### Class: MutagenModule

An Autofac `Module` that registers all standard Mutagen services for dependency injection.

```csharp
public class MutagenModule : Autofac.Module
```

**Inherits:** `Autofac.Module`

#### Registered Services

When loaded into an Autofac `ContainerBuilder`, this module registers implementations for the following service categories:

| Service Area | Key Interfaces Registered | Description |
|-------------|--------------------------|-------------|
| **Archives** | `IArchiveReaderProvider` | Reading and extracting Bethesda archive files (BSA/BA2). |
| **Fonts** | `IGetFontConfig` | Font configuration queries. |
| **Game Detection** | `IDataDirectoryLookup`, `IGameDirectoryLookup` | Locating game installations and data directories. |
| **Implicit Masters** | `IImplicitBaseMasterProvider` | Resolving implicit master dependencies. |
| **Load Order** | `ILoadOrderWriter` | Writing and managing load order files. |
| **Mod Activation** | `IModActivator` | Activating/deactivating mods. |
| **INI Files** | `IIniPathLookup` | Locating game INI configuration files. |
| **Mod I/O** | `IModFilesMover` | Moving mod files between directories. |
| **Compaction** | `IModCompactor`, `IRecordCompactionCompatibilityDetector` | Compacting mods and checking compaction compatibility. |
| **Masters** | `IMasterReferenceReaderFactory` | Creating master reference readers. |
| **Environments** | `IGameEnvironmentProvider<T>`, `IGameEnvironmentProvider<T, T>` | Providing game environment contexts (generic registrations). |
| **Load Order Import** | `ILoadOrderImporter<T>` | Importing load orders (generic registration). |
| **Mod Import** | `IModImporter<T>` | Importing mods (generic registration). |
| **Assets** | `IAssetProvider`, `GameAssetProvider`, `DataDirectoryAssetProvider`, `ArchiveAssetProvider` | Game asset resolution from data directories and archives. |
| **Archive Listings** | `CachedArchiveListingDetailsProvider` | Cached archive listing details. |

#### Usage

```csharp
using Autofac;
using Mutagen.Bethesda.Autofac;

var builder = new ContainerBuilder();
builder.RegisterModule<MutagenModule>();

// Register your own services...
builder.RegisterType<MyService>().AsSelf();

var container = builder.Build();

// Resolve Mutagen services
var archiveReader = container.Resolve<IArchiveReaderProvider>();
var gameLocator = container.Resolve<IGameDirectoryLookup>();
```

#### Registration Strategy

The module uses assembly scanning with namespace filtering via Noggog.Autofac conventions:

1. **Assembly scanning:** Scans the assembly containing `IArchiveReaderProvider` for types in specific namespaces.
2. **Namespace filtering:** Only registers types from the `DI` sub-namespaces of core Mutagen service areas (Archives, Assets, Environments, Fonts, Inis, Installs, Plugins.Analysis, Plugins.Implicit, Plugins.IO, Plugins.Masters, Plugins.Order, Plugins.Records, Plugins.Utility).
3. **Exclusions:** `FontProvider` is explicitly excluded from registration.
4. **Generic registrations:** Open generic types like `GameEnvironmentProvider<>`, `LoadOrderImporter<>`, and `ModImporter<>` are registered separately.
5. **Concrete registrations:** `GameLocatorLookupCache`, `GameAssetProvider`, `DataDirectoryAssetProvider`, `ArchiveAssetProvider`, and `CachedArchiveListingDetailsProvider` are registered explicitly.

#### Protected Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `Load` | `protected override void Load(ContainerBuilder builder)` | Registers all Mutagen services into the Autofac container. Called automatically when the module is registered via `RegisterModule<MutagenModule>()`. |
