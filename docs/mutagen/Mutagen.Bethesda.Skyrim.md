# Mutagen.Bethesda.Skyrim

Game-specific record types and mod handling for The Elder Scrolls V: Skyrim (all editions). This project contains mostly **generated** record type definitions created by the Loqui code generator, along with handwritten partial classes that provide custom binary parsing, flags, enums, and special behavior.

**Namespace:** `Mutagen.Bethesda.Skyrim`

---

## Generated Code Pattern

Every major record type follows a consistent generated code pattern produced by Loqui:

### Per-Record Type Artifacts

For a record type named `Foo`, the following are generated:

| Artifact | Description |
|----------|-------------|
| `Foo` | Mutable class inheriting from `SkyrimMajorRecord`. Implements `IFooInternal` and `IEquatable<IFooGetter>`. |
| `IFooGetter` | Read-only getter interface. All properties are read-only. Used when you only need to inspect data. |
| `IFoo` | Mutable setter interface extending `IFooGetter`. Used when you need to modify data. |
| `IFooInternal` | Internal setter interface used by the binary parsing infrastructure. Not intended for public use. |
| `FooBinaryOverlay` | Lazy binary overlay class (internal). Reads data on-demand from raw binary data without full deserialization. Created via `CreateFromBinaryOverlay`. |
| `FooBinaryCreateTranslation` | Handles deserialization from binary plugin data into the mutable `Foo` class. |
| `FooBinaryWriteTranslation` | Handles serialization from `IFooGetter` back to binary plugin data. |
| `FooSetterCommon` / `FooCommon` | Internal helper classes for deep copy, equality, and asset link operations. |

### Key Patterns in Generated Code

- **FormLinks**: References to other records use `IFormLinkGetter<T>` (read) and `IFormLinkNullable<T>` (nullable). Resolved via link caches.
- **TranslatedString**: Name and description fields that support localization use `TranslatedString` / `ITranslatedStringGetter`.
- **Aspects**: Records implement shared aspect interfaces (e.g., `INamed`, `IObjectBounded`, `IScripted`, `IHasVoiceType`).
- **ExtendedList<T>**: Used for lists of sub-records (e.g., items in containers, effects on spells).
- **ObjectBounds**: 3D bounding box (`ObjectBounds`) present on most placed/renderable records.
- **VirtualMachineAdapter**: Papyrus script attachment data, present on scriptable records.

### XML Definitions

Each record type has a corresponding `.xml` file (e.g., `Weapon.xml`) that defines the record schema for the Loqui code generator. These drive the `_Generated.cs` output.

### Handwritten Partials

Non-generated `.cs` files (without `_Generated` suffix) contain custom behavior:
- Custom `MajorFlag` and `Flag` enums specific to each record
- Custom binary parsing/writing for fields that don't follow standard patterns
- Binary overlay implementations for complex subrecord structures
- Interface implementations (e.g., `IEnchantable`, `IHasVoiceType`)

---

## SkyrimMod Class

The main mod class representing a `.esp`/`.esm`/`.esl` file for Skyrim.

**File:** `Records/SkyrimMod.cs` + `Records/SkyrimMod_Generated.cs`

```csharp
public partial class SkyrimMod : AMod, ISkyrimMod
```

### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `ModHeader` | `SkyrimModHeader` | Plugin header (author, description, masters, flags, version) |
| `IsMaster` | `bool` | Whether the mod has the Master flag set |
| `IsSmallMaster` | `bool` | Whether the mod is a light/small master (.esl) |
| `CanBeSmallMaster` | `bool` | Always `true` for Skyrim SE+ |
| `CanBeMediumMaster` | `bool` | Always `false` (Skyrim does not support medium masters) |
| `OverriddenForms` | `IReadOnlyList<IFormLinkGetter<IMajorRecordGetter>>?` | List of forms overridden by this plugin |
| `ListsOverriddenForms` | `bool` | Always `true` |

### Supported Releases

The `SkyrimRelease` enum maps to:
- `SkyrimLE` - Skyrim Legendary Edition
- `SkyrimSE` - Skyrim Special Edition (Steam)
- `SkyrimSEGog` - Skyrim Special Edition (GOG)
- `SkyrimVR` - Skyrim VR
- `EnderalLE` - Enderal (LE)
- `EnderalSE` - Enderal Special Edition

### Builder API

```csharp
// Reading
var mod = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
    .FromPath(path)
    .Mutable();           // Full deserialization
    // or .Readonly();    // Binary overlay (lazy, disposable)

// Writing
mod.BeginWrite
    .ToPath(outputPath)
    .WithAutoSplit()      // Optional: auto-split large mods
    .Write();
```

### Parallel Writing

`SkyrimModCommon` contains parallel write methods for performance-critical record groups:
- `WriteCellsParallel` - Parallel serialization of cell blocks/sub-blocks
- `WriteWorldspacesParallel` - Parallel serialization of worldspace hierarchies
- `WriteDialogTopicsParallel` - Parallel serialization of dialog topics

### Custom Record Count Logic

The `GetCustomRecordCount` method tallies group records for cells (persistent/temporary children, navigation meshes, landscape), worldspaces (cell blocks/sub-blocks), and dialog topics.

---

## Record Group Structure

`SkyrimMod` organizes records into typed groups via `SkyrimGroup<T>`. These are the top-level groups in load order:

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `GameSettings` | `GameSetting` | Game configuration values (float, int, string, bool) |
| `Keywords` | `Keyword` | Tagging keywords used by the game engine |
| `LocationReferenceTypes` | `LocationReferenceType` | Types of location references |
| `Actions` | `ActionRecord` | Gameplay actions |
| `TextureSets` | `TextureSet` | Texture set definitions |
| `Globals` | `Global` | Global variables (float, int, short) |
| `Classes` | `Class` | Character classes |
| `Factions` | `Faction` | Factions and crime/vendor data |
| `HeadParts` | `HeadPart` | Character head part meshes |
| `Hairs` | `Hair` | Legacy hair records |
| `Eyes` | `Eyes` | Eye appearance records |
| `Races` | `Race` | Playable and non-playable races |
| `SoundMarkers` | `SoundMarker` | Sound emitter markers |
| `AcousticSpaces` | `AcousticSpace` | Environmental acoustic settings |
| `MagicEffects` | `MagicEffect` | Base magic effect definitions |
| `LandscapeTextures` | `LandscapeTexture` | Terrain texture definitions |
| `ObjectEffects` | `ObjectEffect` | Enchantments for objects |
| `Spells` | `Spell` | Spells with effect lists |
| `Scrolls` | `Scroll` | Scroll items (castable spells) |
| `Activators` | `Activator` | Activatable world objects |
| `TalkingActivators` | `TalkingActivator` | Activators with voice/dialog |
| `Armors` | `Armor` | Armor items |
| `Books` | `Book` | Books, notes, and skill books |
| `Containers` | `Container` | Containers with inventories |
| `Doors` | `Door` | Door objects |
| `Ingredients` | `Ingredient` | Alchemy ingredients |
| `Lights` | `Light` | Light sources |
| `MiscItems` | `MiscItem` | Miscellaneous items |
| `AlchemicalApparatuses` | `AlchemicalApparatus` | Legacy alchemy apparatus |
| `Statics` | `Static` | Static mesh objects |
| `MoveableStatics` | `MoveableStatic` | Havok-enabled static objects |
| `Grasses` | `Grass` | Grass definitions |
| `Trees` | `Tree` | Tree objects |
| `Florae` | `Flora` | Harvestable plants |
| `Furniture` | `Furniture` | Furniture (sit/sleep markers) |
| `Weapons` | `Weapon` | Weapon items |
| `Ammunitions` | `Ammunition` | Arrows and bolts |
| `Npcs` | `Npc` | Non-player characters |
| `LeveledNpcs` | `LeveledNpc` | Leveled NPC lists |
| `Keys` | `Key` | Key items |
| `Ingestibles` | `Ingestible` | Potions and food |
| `IdleMarkers` | `IdleMarker` | Idle animation markers |
| `ConstructibleObjects` | `ConstructibleObject` | Crafting recipes |
| `Projectiles` | `Projectile` | Projectile definitions |
| `Hazards` | `Hazard` | Hazard effect zones |
| `SoulGems` | `SoulGem` | Soul gem items |
| `LeveledItems` | `LeveledItem` | Leveled item lists |
| `Weathers` | `Weather` | Weather definitions |
| `Climates` | `Climate` | Climate configurations |
| `ShaderParticleGeometries` | `ShaderParticleGeometry` | Shader particle effects |
| `VisualEffects` | `VisualEffect` | Visual effect definitions |
| `Regions` | `Region` | World regions |
| `NavigationMeshInfoMaps` | `NavigationMeshInfoMap` | Navigation mesh info |
| `Worldspaces` | `Worldspace` | World spaces (contain cells) |
| `Cells` | (List group) | Interior cells organized in blocks/sub-blocks |
| `DialogTopics` | `DialogTopic` | Dialog topic trees |
| `Quests` | `Quest` | Quest definitions |
| `IdleAnimations` | `IdleAnimation` | Idle animation definitions |
| `Packages` | `Package` | AI packages |
| `CombatStyles` | `CombatStyle` | NPC combat behavior |
| `LoadScreens` | `LoadScreen` | Loading screen content |
| `LeveledSpells` | `LeveledSpell` | Leveled spell lists |
| `AnimatedObjects` | `AnimatedObject` | Objects with animations |
| `Waters` | `Water` | Water type definitions |
| `EffectShaders` | `EffectShader` | Effect shader visuals |
| `Explosions` | `Explosion` | Explosion definitions |
| `Debris` | `Debris` | Debris object collections |
| `ImageSpaces` | `ImageSpace` | Post-processing settings |
| `ImageSpaceAdapters` | `ImageSpaceAdapter` | Animated image space transitions |
| `FormLists` | `FormList` | Generic lists of forms |
| `Perks` | `Perk` | Perk trees and effects |
| `BodyParts` | `BodyPartData` | Body part hit data |
| `AddonNodes` | `AddonNode` | Add-on node definitions |
| `ActorValueInformation` | `ActorValueInformation` | Actor value definitions |
| `CameraShots` | `CameraShot` | Camera shot types |
| `CameraPaths` | `CameraPath` | Camera path sequences |
| `VoiceTypes` | `VoiceType` | Voice type assignments |
| `MaterialTypes` | `MaterialType` | Material types for impacts |
| `Impacts` | `Impact` | Impact effect definitions |
| `ImpactDataSets` | `ImpactDataSet` | Sets of impact effects |
| `ArmorAddons` | `ArmorAddon` | Armor mesh/texture addons |
| `EncounterZones` | `EncounterZone` | Level-scaled encounter zones |
| `Locations` | `Location` | Location records |
| `Messages` | `Message` | Message box definitions |
| `DefaultObjectManagers` | `DefaultObjectManager` | Default object assignments |
| `LightingTemplates` | `LightingTemplate` | Interior lighting presets |
| `MusicTypes` | `MusicType` | Music type configurations |
| `Footsteps` | `Footstep` | Footstep sound definitions |
| `FootstepSets` | `FootstepSet` | Sets of footstep sounds |
| `StoryManagerBranchNodes` | `StoryManagerBranchNode` | Story manager branch logic |
| `StoryManagerQuestNodes` | `StoryManagerQuestNode` | Story manager quest assignments |
| `StoryManagerEventNodes` | `StoryManagerEventNode` | Story manager event triggers |
| `DialogBranches` | `DialogBranch` | Dialog branch definitions |
| `MusicTracks` | `MusicTrack` | Individual music tracks |
| `DialogViews` | `DialogView` | Dialog view configurations |
| `WordsOfPower` | `WordOfPower` | Dragon shout words |
| `Shouts` | `Shout` | Dragon shouts (3 words) |
| `EquipTypes` | `EquipType` | Equipment slot types |
| `Relationships` | `Relationship` | NPC relationship records |
| `Scenes` | `Scene` | Scripted scene definitions |
| `AssociationTypes` | `AssociationType` | NPC association types |
| `Outfits` | `Outfit` | NPC outfit definitions |
| `ArtObjects` | `ArtObject` | Art object definitions |
| `MaterialObjects` | `MaterialObject` | Material object definitions |
| `MovementTypes` | `MovementType` | NPC movement types |
| `SoundDescriptors` | `SoundDescriptor` | Sound descriptor definitions |
| `DualCastData` | `DualCastData` | Dual-cast magic data |
| `SoundCategories` | `SoundCategory` | Sound category definitions |
| `SoundOutputModels` | `SoundOutputModel` | Sound output configurations |
| `CollisionLayers` | `CollisionLayer` | Physics collision layers |
| `Colors` | `ColorRecord` | Named color definitions |
| `ReverbParameters` | `ReverbParameters` | Audio reverb settings |
| `VolumetricLightings` | `VolumetricLighting` | Volumetric lighting settings |
| `LensFlares` | `LensFlare` | Lens flare effects |

---

## Major Record Type Catalog

### Characters and Creatures

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Npc` | Non-player characters | `Race`, `Name`, `Configuration`, `Factions`, `Level`, `AIData`, `Packages`, `Items`, `Class`, `Voice` | `INpcGetter` |
| `Race` | Playable and creature races | `Name`, `Flags`, `Skin`, `Spells`, `HeadData`, `BodyData`, `MovementTypes`, `Voices` | `IRaceGetter` |
| `Class` | Character classes | `Name`, `Teaches`, `MaxTraining`, `ActorValues` | `IClassGetter` |
| `Faction` | Factions with ranks and crime data | `Name`, `Flags`, `Relations`, `Ranks`, `CrimeValues`, `VendorValues` | `IFactionGetter` |
| `Relationship` | Relationships between NPCs | `Parent`, `Child`, `Rank`, `AssociationType` | `IRelationshipGetter` |
| `VoiceType` | Voice type for dialog | `Flags` | `IVoiceTypeGetter` |
| `LeveledNpc` | Leveled NPC lists | `Entries`, `ChanceNone`, `Flags` | `ILeveledNpcGetter` |

### Items and Equipment

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Weapon` | Weapons | `Name`, `ObjectBounds`, `ObjectEffect`, `WeaponData`, `CriticalData`, `Template`, `Keywords` | `IWeaponGetter` |
| `Armor` | Armor pieces | `Name`, `ObjectBounds`, `ObjectEffect`, `BodyTemplate`, `ArmorRating`, `Value`, `Weight`, `Keywords` | `IArmorGetter` |
| `ArmorAddon` | Armor visual addons | `WorldModel`, `FirstPersonModel`, `Race`, `AdditionalRaces`, `BodyTemplate` | `IArmorAddonGetter` |
| `Ammunition` | Arrows and bolts | `Name`, `ObjectBounds`, `Projectile`, `Damage`, `Value`, `Flags` | `IAmmunitionGetter` |
| `Book` | Books and skill books | `Name`, `Text`, `TeachTarget`, `Value`, `Weight`, `Flags`, `ObjectEffect` | `IBookGetter` |
| `Ingredient` | Alchemy ingredients | `Name`, `Value`, `Weight`, `Effects` | `IIngredientGetter` |
| `Ingestible` | Potions and food | `Name`, `Value`, `Weight`, `Effects`, `Flags` | `IIngestibleGetter` |
| `MiscItem` | Miscellaneous items | `Name`, `Value`, `Weight`, `Keywords` | `IMiscItemGetter` |
| `Key` | Key items | `Name`, `Value`, `Weight` | `IKeyGetter` |
| `SoulGem` | Soul gems | `Name`, `Value`, `Weight`, `ContainedSoul`, `MaximumCapacity` | `ISoulGemGetter` |
| `Scroll` | Scroll items | `Name`, `ObjectBounds`, `Effects`, `CastType`, `TargetType` | `IScrollGetter` |
| `Container` | Containers | `Name`, `Items`, `Flags`, `Weight`, `OpenSound`, `CloseSound` | `IContainerGetter` |
| `LeveledItem` | Leveled item lists | `Entries`, `ChanceNone`, `Flags` | `ILeveledItemGetter` |
| `LeveledSpell` | Leveled spell lists | `Entries`, `ChanceNone`, `Flags` | `ILeveledSpellGetter` |
| `Outfit` | NPC outfits | `Items` | `IOutfitGetter` |
| `ConstructibleObject` | Crafting recipes | `Items`, `Conditions`, `CreatedObject`, `WorkbenchKeyword`, `CreatedObjectCount` | `IConstructibleObjectGetter` |
| `AlchemicalApparatus` | Legacy alchemy apparatus | `Name`, `Value`, `Weight`, `Quality` | `IAlchemicalApparatusGetter` |
| `FormList` | Generic form lists | `Items` | `IFormListGetter` |

### Magic

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Spell` | Spells | `Name`, `Type`, `CastType`, `TargetType`, `Effects`, `BaseCost`, `Flags` | `ISpellGetter` |
| `MagicEffect` | Base magic effects | `Name`, `Flags`, `Archetype`, `School`, `BaseCost`, `Conditions`, `AssociatedItem` | `IMagicEffectGetter` |
| `ObjectEffect` | Enchantments | `Name`, `EnchantType`, `Effects`, `TargetType`, `CastType` | `IObjectEffectGetter` |
| `Perk` | Perk tree entries | `Name`, `Description`, `Effects`, `Conditions`, `Trait`, `Level`, `Playable` | `IPerkGetter` |
| `Shout` | Dragon shouts (3 words) | `Name`, `Words`, `Description` | `IShoutGetter` |
| `WordOfPower` | Individual shout words | `Name`, `Translation` | `IWordOfPowerGetter` |
| `DualCastData` | Dual-cast settings | `Hiteff`, `Explosion`, `EffectShader`, `InheritScale` | `IDualCastDataGetter` |
| `Hazard` | Hazard effect areas | `Name`, `Flags`, `Limit`, `Radius`, `Lifetime`, `Spell`, `Light` | `IHazardGetter` |

### World and Cells

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Worldspace` | World spaces | `Name`, `TopCell`, `SubCells`, `Music`, `Climate`, `Water`, `LODWater`, `Flags` | `IWorldspaceGetter` |
| `Cell` | Interior/exterior cells | `Flags`, `Lighting`, `Water`, `Owner`, `Persistent`, `Temporary`, `NavigationMeshes`, `Landscape` | `ICellGetter` |
| `Landscape` | Terrain heightmap/textures | `VertexHeightMap`, `VertexNormals`, `Layers` | `ILandscapeGetter` |
| `NavigationMesh` | Pathfinding mesh | `Data`, `NVNM` | `INavigationMeshGetter` |
| `NavigationMeshInfoMap` | NavMesh metadata | `NavMeshInfos` | `INavigationMeshInfoMapGetter` |
| `PlacedObject` | Placed references (REFR) | `Base`, `Position`, `Rotation`, `Scale`, `EnableParent`, `Ownership`, `Primitive` | `IPlacedObjectGetter` |
| `PlacedNpc` | Placed NPC references (ACHR) | `Base`, `Position`, `Rotation`, `Scale`, `EnableParent` | `IPlacedNpcGetter` |
| `Region` | World regions | `MapName`, `Data` (objects, weather, grasses, sounds, map, land) | `IRegionGetter` |
| `Location` | Location data | `Name`, `Keywords`, `ParentLocation`, `Factions`, `WorldLocationMarker` | `ILocationGetter` |
| `EncounterZone` | Level-scaled zones | `Owner`, `Location`, `Rank`, `MinLevel`, `MaxLevel`, `Flags` | `IEncounterZoneGetter` |

### Placed Object Subtypes (Traps/Projectiles)

| Record Type | Description | Getter Interface |
|-------------|-------------|-----------------|
| `PlacedArrow` | Placed arrow projectile | `IPlacedArrowGetter` |
| `PlacedBarrier` | Placed barrier trap | `IPlacedBarrierGetter` |
| `PlacedBeam` | Placed beam trap | `IPlacedBeamGetter` |
| `PlacedCone` | Placed cone trap | `IPlacedConeGetter` |
| `PlacedFlame` | Placed flame trap | `IPlacedFlameGetter` |
| `PlacedHazard` | Placed hazard | `IPlacedHazardGetter` |
| `PlacedMissile` | Placed missile projectile | `IPlacedMissileGetter` |
| `PlacedTrap` | Placed trap (generic) | `IPlacedTrapGetter` |

All placed types implement the `IPlaced` interface and share `Base`, `Position`, `Rotation` fields.

### Quests and Dialog

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Quest` | Quest definitions | `Name`, `Flags`, `Type`, `Stages`, `Objectives`, `Aliases`, `DialogConditions` | `IQuestGetter` |
| `DialogTopic` | Dialog topics | `Name`, `Priority`, `Branch`, `TopicFlags`, `Category`, `Subtype`, `Responses` | `IDialogTopicGetter` |
| `DialogBranch` | Dialog branches | `Quest`, `Category`, `Flags`, `StartingTopic` | `IDialogBranchGetter` |
| `DialogResponses` | Dialog response entries | `Conditions`, `Responses`, `Script`, `Flags` | `IDialogResponsesGetter` |
| `DialogView` | Dialog view layout | `Quest`, `Branches` | `IDialogViewGetter` |
| `Scene` | Scripted scenes | `Phases`, `Actors`, `Actions`, `Flags`, `Quest` | `ISceneGetter` |
| `Package` | AI packages | `Flags`, `Type`, `Conditions`, `Data`, `IdleAnimations`, `Schedule` | `IPackageGetter` |
| `IdleAnimation` | Idle animations | `Conditions`, `Filename`, `AnimationEvent`, `RelatedIdleAnimations` | `IIdleAnimationGetter` |

### Story Manager

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `StoryManagerBranchNode` | Branch logic node | `Conditions`, `Flags`, `Children` | `IStoryManagerBranchNodeGetter` |
| `StoryManagerQuestNode` | Quest assignment node | `Quest`, `Conditions`, `Flags`, `Hours`, `Children` | `IStoryManagerQuestNodeGetter` |
| `StoryManagerEventNode` | Event trigger node | `Type`, `Conditions`, `Children` | `IStoryManagerEventNodeGetter` |

### Visual and Audio

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Weather` | Weather definitions | `CloudTextures`, `FogDistances`, `Colors`, `Sounds`, `ImageSpaces`, `Precipitation` | `IWeatherGetter` |
| `Climate` | Climate settings | `Weathers`, `SunTexture`, `SunGlareTexture`, `Moons` | `IClimateGetter` |
| `ImageSpace` | Post-processing | `HDR`, `Cinematic`, `Tint`, `DepthOfField` | `IImageSpaceGetter` |
| `ImageSpaceAdapter` | Animated post-processing | Keyframed HDR/cinematic/tint transitions | `IImageSpaceAdapterGetter` |
| `EffectShader` | Effect shader visuals | `Flags`, `TextureFile`, `FillColor`, `RimColor`, `EdgeColor` | `IEffectShaderGetter` |
| `VisualEffect` | Visual effects | `EffectArt`, `Flags` | `IVisualEffectGetter` |
| `LightingTemplate` | Interior lighting | `AmbientColor`, `DirectionalColor`, `FogColor`, `FogDistances` | `ILightingTemplateGetter` |
| `VolumetricLighting` | Volumetric light | Intensity, scatter, phase function parameters | `IVolumetricLightingGetter` |
| `LensFlare` | Lens flare effects | `Sprites` | `ILensFlareGetter` |
| `Explosion` | Explosions | `Force`, `Damage`, `Radius`, `ImageSpaceModifier`, `Light`, `Sound`, `Flags` | `IExplosionGetter` |
| `Projectile` | Projectile behavior | `Flags`, `Type`, `Speed`, `Gravity`, `Explosion`, `Light`, `Sound` | `IProjectileGetter` |
| `Debris` | Debris collections | `Models` | `IDebrisGetter` |
| `ShaderParticleGeometry` | Shader particles | `Type`, `Density`, `Velocity`, `Rotation`, `Color` | `IShaderParticleGeometryGetter` |

### Audio

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `SoundMarker` | Sound emitters | `SoundDescriptor` | `ISoundMarkerGetter` |
| `AcousticSpace` | Acoustic environments | `AmbientSound`, `Region`, `ReverbType` | `IAcousticSpaceGetter` |
| `SoundDescriptor` | Sound definitions | `Conditions`, `Category`, `OutputModel`, `SoundFiles` | `ISoundDescriptorGetter` |
| `SoundOutputModel` | Output configuration | `Flags`, `Attenuations`, `ReverbSend` | `ISoundOutputModelGetter` |
| `SoundCategory` | Sound categories | `Name`, `ParentCategory`, `Flags`, `StaticVolumeMultiplier` | `ISoundCategoryGetter` |
| `ReverbParameters` | Reverb settings | `DecayTime`, `Reflections`, `ReverbDelay`, `Diffusion` | `IReverbParametersGetter` |
| `MusicType` | Music configurations | `Flags`, `Priority`, `Tracks`, `FadeTime` | `IMusicTypeGetter` |
| `MusicTrack` | Music tracks | `Type`, `Duration`, `FadeOut`, `Filename`, `Tracks` | `IMusicTrackGetter` |
| `Footstep` | Footstep sounds | `ImpactDataSet`, `Tag` | `IFootstepGetter` |
| `FootstepSet` | Footstep sound sets | `WalkForward`, `RunForward`, `WalkForwardAlternate`, etc. | `IFootstepSetGetter` |

### Statics and World Objects

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Static` | Static meshes | `ObjectBounds`, `Model`, `MaxAngle`, `Material`, `Flags` | `IStaticGetter` |
| `MoveableStatic` | Physics-enabled statics | `ObjectBounds`, `Model`, `Flags` | `IMoveableStaticGetter` |
| `Activator` | Activatable objects | `Name`, `Model`, `Flags`, `WaterType`, `ActivateTextOverride`, `Keywords` | `IActivatorGetter` |
| `TalkingActivator` | Dialog activators | `Name`, `Model`, `VoiceType` | `ITalkingActivatorGetter` |
| `Door` | Doors | `Name`, `Model`, `OpenSound`, `CloseSound`, `Flags` | `IDoorGetter` |
| `Furniture` | Furniture/markers | `Name`, `Model`, `Flags`, `MarkerParameters`, `Keywords` | `IFurnitureGetter` |
| `Light` | Light sources | `Name`, `Color`, `Radius`, `Flags`, `FalloffExponent`, `FOV`, `Value` | `ILightGetter` |
| `Tree` | Tree objects | `Model`, `SpeedTreeSeeds`, `BillboardDimensions` | `ITreeGetter` |
| `Grass` | Grass definitions | `Model`, `Density`, `MinSlope`, `MaxSlope`, `WavePeriod` | `IGrassGetter` |
| `Flora` | Harvestable plants | `Name`, `Model`, `Ingredient`, `HarvestSound` | `IFloraGetter` |
| `AnimatedObject` | Animated objects | `Model`, `UnloadEvent` | `IAnimatedObjectGetter` |
| `AddonNode` | Add-on nodes | `NodeIndex`, `Sound`, `MasterParticleSystemCap` | `IAddonNodeGetter` |
| `TextureSet` | Texture sets | `Diffuse`, `Normal`, `EnvironmentMask`, `Glow`, `Parallax`, `Environment` | `ITextureSetGetter` |
| `ArtObject` | Art objects | `Model`, `Type` | `IArtObjectGetter` |
| `MaterialObject` | Material objects | `Model`, `MaterialType`, `Properties` | `IMaterialObjectGetter` |
| `LandscapeTexture` | Terrain textures | `TextureSet`, `MaterialType`, `HavokData`, `Grasses` | `ILandscapeTextureGetter` |

### Game Configuration

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `GameSetting` | Game settings (abstract base) | `EditorID`, `SettingType` | `IGameSettingGetter` |
| `GameSettingFloat` | Float game setting | `Data` (float value) | `IGameSettingFloatGetter` |
| `GameSettingInt` | Integer game setting | `Data` (int value) | `IGameSettingIntGetter` |
| `GameSettingString` | String game setting | `Data` (string value) | `IGameSettingStringGetter` |
| `GameSettingBool` | Boolean game setting | `Data` (bool value) | `IGameSettingBoolGetter` |
| `Global` | Global variables (abstract base) | `RawFloat` | `IGlobalGetter` |
| `GlobalFloat` | Float global variable | `Data` | `IGlobalFloatGetter` |
| `GlobalInt` | Integer global variable | `Data` | `IGlobalIntGetter` |
| `GlobalShort` | Short global variable | `Data` | `IGlobalShortGetter` |
| `Keyword` | Keywords | `Name`, `Color`, `Type` | `IKeywordGetter` |
| `LocationReferenceType` | Location reference types | `Color` | `ILocationReferenceTypeGetter` |
| `ActionRecord` | Gameplay actions | `Color` | `IActionRecordGetter` |
| `DefaultObjectManager` | Default objects | `Objects` | `IDefaultObjectManagerGetter` |
| `EquipType` | Equip slot types | `SlotParents`, `Flags` | `IEquipTypeGetter` |
| `CollisionLayer` | Collision layers | `Name`, `Color`, `CollidesWith` | `ICollisionLayerGetter` |
| `ColorRecord` | Color definitions | `Color` | `IColorRecordGetter` |
| `MaterialType` | Material types | `Parent`, `Name`, `HavokDisplayColor` | `IMaterialTypeGetter` |

### Miscellaneous

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Water` | Water types | `Name`, `Opacity`, `Properties`, `LinearVelocity`, `Flags`, `DamagePerSecond` | `IWaterGetter` |
| `CombatStyle` | Combat styles | `GeneralMult`, `MeleeMult`, `CloseMult`, `LongRangeMult`, `FlightMult` | `ICombatStyleGetter` |
| `LoadScreen` | Loading screens | `Description`, `Conditions`, `DisplayModel` | `ILoadScreenGetter` |
| `Message` | Message boxes | `Name`, `Description`, `Flags`, `Buttons`, `Conditions` | `IMessageGetter` |
| `BodyPartData` | Body part definitions | `Name`, `Model`, `Parts` | `IBodyPartDataGetter` |
| `ActorValueInformation` | Actor values | `Name`, `Description` | `IActorValueInformationGetter` |
| `CameraShot` | Camera shots | `Action`, `Location`, `Target`, `Flags`, `TimeMultiplier` | `ICameraShotGetter` |
| `CameraPath` | Camera paths | `Conditions`, `Shots`, `Flags` | `ICameraPathGetter` |
| `Impact` | Impact effects | `DecalData`, `TextureSet`, `HazardRecord`, `SoundDescriptors` | `IImpactGetter` |
| `ImpactDataSet` | Impact data sets | `Impacts` | `IImpactDataSetGetter` |
| `MovementType` | Movement types | `Name`, `DefaultData`, `SpeedData` | `IMovementTypeGetter` |
| `AssociationType` | Association types | `MaleParentTitle`, `FemaleParentTitle`, `MaleChildTitle`, `FemaleChildTitle` | `IAssociationTypeGetter` |
| `Eyes` | Eye definitions | `Name`, `Flags`, `Model` | `IEyesGetter` |
| `HeadPart` | Head parts | `Name`, `Flags`, `Type`, `Model`, `ExtraParts`, `TextureSet` | `IHeadPartGetter` |

---

## Handwritten Partials (Notable Custom Behavior)

The following handwritten partial classes contain important custom logic beyond the generated code:

### Cell (`Cell.cs`)
- Defines `MajorFlag` (Persistent, OffLimits, CantWait) and `Flag` (IsInteriorCell, HasWater, ShowSky, etc.)
- Complex custom binary parsing for cell children groups (persistent/temporary children)
- Handles navigation meshes and landscape as special temporary children
- Binary overlay manages group data lazily with position tracking for persistent/temporary sections
- Placed object polymorphism: reads ACHR, REFR, PARW, PBAR, PBEA, PCON, PFLA, PHZD, PMIS, PGRE record types

### Npc (`Npc.cs`)
- Defines `MajorFlag.BleedoutOverride`
- Implements `IHasVoiceTypeGetter` for voice type access
- Asset link resolution for face generation data (face tint DDS and face geometry NIF files)
- Custom DATA marker binary handling (skip on read, write empty on write)

### Weapon (`Weapon.cs`)
- Defines `MajorFlag.NonPlayable`
- Implements `IEnchantableGetter` for enchantment access

### Armor (`Armor.cs`)
- Defines `MajorFlag` (NonPlayable, Shield)
- Implements `IEnchantableGetter`
- Custom binary parsing for `BodyTemplate` subrecord

### Quest (`Quest.cs`)
- Defines `Flag` (StartGameEnabled, RunOnce, AllowRepeatedStages, etc.)
- Defines `TypeEnum` (None, MainQuest, MageGuild, ThievesGuild, DarkBrotherhood, CompanionQuests, Misc, Daedric, SideQuest, CivilWar, Vampire, Dragonborn)
- Inferred asset link resolution for quest script fragments

### GameSetting (`GameSetting.cs`)
- Abstract base with `SettingType` property
- Custom `CreateFromBinary` factory that dispatches to `GameSettingFloat`, `GameSettingInt`, `GameSettingString`, or `GameSettingBool` based on EditorID prefix
- EditorID auto-correction to ensure proper type prefix

### SkyrimMod (`SkyrimMod.cs`)
- Master flag management via `SkyrimModHeader.HeaderFlag`
- Lower FormID range support for SE/GOG/EnderalSE
- Builder pattern for reading (mutable vs overlay) and writing (with auto-split support)
- Inferred asset links for translation files when the Localized flag is set
- Parallel write infrastructure for cells, worldspaces, and dialog topics

### Worldspace (`Worldspace.cs`)
- Custom binary handling for world space children (top cell, sub-cells)
- Cell block/sub-block hierarchy parsing

### Region (`Region.cs`) and Region Data Types
- `RegionData` abstract base with typed subtypes: `RegionObjects`, `RegionWeather`, `RegionMap`, `RegionLand`, `RegionGrasses`, `RegionSounds`
- Custom binary parsing for polymorphic region data entries

### MagicEffect (`MagicEffect.cs`)
- Complex archetype system with abstract `AMagicEffectArchetype` and typed subtypes
- Subtypes include: `MagicEffectArchetype`, `MagicEffectBoundArchetype`, `MagicEffectCloakArchetype`, `MagicEffectEnhanceWeaponArchetype`, `MagicEffectGuideArchetype`, `MagicEffectLightArchetype`, `MagicEffectSpawnHazardArchetype`, `MagicEffectSummonCreatureArchetype`, `MagicEffectVampireArchetype`, `MagicEffectWerewolfArchetype`, `MagicEffectPeakValueMod`

### Package (`Package.cs`) and AI System
- `APackageData` abstract base with typed subtypes: `PackageDataBool`, `PackageDataFloat`, `PackageDataInt`, `PackageDataLocation`, `PackageDataObjectList`, `PackageDataTarget`, `PackageDataTopic`
- `APackageTarget` abstract base for target specification
- Complex package branch/event/idle structure

### Perk (`Perk.cs`)
- `APerkEffect` abstract base with entry point subtypes
- `APerkEntryPointEffect` for modify-value and modify-actor-value effects
- Script fragment handling via `PerkAdapter` and `PerkScriptFragments`

---

## Key Enums

### Skill
```
OneHanded, TwoHanded, Archery, Block, Smithing, HeavyArmor, LightArmor,
Pickpocket, Lockpicking, Sneak, Alchemy, Speech, Alteration, Conjuration,
Destruction, Illusion, Restoration, Enchanting
```

### BipedObjectFlag (Flags)
```
Head, Hair, Body, Hands, Forearms, Amulet, Ring, Feet, Calves, Shield,
Tail, LongHair, Circlet, Ears, DecapitateHead, Decapitate, FX01
```

### ActorValue
Comprehensive enum covering all Skyrim actor values including health, magicka, stamina, skills, resistances, and derived stats.

### Other Important Enums
- `Aggression` - Unaggressive through Frenzied
- `Confidence` - Cowardly through Foolhardy
- `Mood` - Neutral, Angry, Fear, Happy, Sad, Surprised, Puzzled, Disgusted
- `Alignment` - Good, Neutral, Evil, Friend, Ally
- `CastType` - ConstantEffect, FireAndForget, Concentration
- `TargetType` - Self, Touch, Aimed, TargetActor, TargetLocation
- `SpellType` - Spell, Disease, Power, LesserPower, Ability, Poison, Addiction, Voice
- `ArmorType` - LightArmor, HeavyArmor, Clothing
- `WeaponAnimationType` - HandToHand through Crossbow
- `SoundLevel` - Loud, Normal, Silent, VeryLoud
- `LockLevel` - VeryEasy through RequiresKey
- `GroupTypeEnum` - Top through CellTemporaryChildren
- `FormType` - All record type identifiers used in conditions

---

## Link Interfaces

Link interfaces in `Interfaces/Link/` define polymorphic groupings:

| Interface | Description | Example Implementors |
|-----------|-------------|---------------------|
| `IItem` | Any inventory item | `Weapon`, `Armor`, `MiscItem`, `Ingestible`, `Book`, `Key`, etc. |
| `IPlaced` | Any placed reference | `PlacedObject`, `PlacedNpc`, `PlacedArrow`, `PlacedTrap`, etc. |
| `IPlacedSimple` | Simple placed references | `PlacedObject`, `PlacedNpc` |
| `INpcSpawn` | NPC or leveled NPC | `Npc`, `LeveledNpc` |
| `IItemOrList` | Item or leveled item | `IItem` implementors plus `LeveledItem` |
| `IMagicItem` | Magic records | `Spell`, `ObjectEffect`, `Scroll`, `Ingestible`, `Ingredient` |
| `IOwner` | Ownership targets | `Faction`, `Npc` |
| `IConstructible` | Craftable items | Items usable in `ConstructibleObject` recipes |
| `ISound` | Sound references | `SoundMarker` |
| `IEmittance` | Light emitters | `Light`, `Region` |
| `IReferenceableObject` | Objects that can be placed | Most item and static record types |
| `IEffectRecord` | Records with effects | `Spell`, `Scroll`, `Ingestible`, `Ingredient`, `ObjectEffect` |
| `ISpellRecord` | Spell-like records | `Spell`, `LeveledSpell` |

---

## Aspect Interfaces

Aspect interfaces in `Interfaces/Aspect/` provide cross-cutting behavior:

| Interface | Description | Methods/Properties |
|-----------|-------------|-------------------|
| `INamed` | Has a name | `Name` (string?) |
| `IObjectBounded` | Has 3D bounds | `ObjectBounds` |
| `IScripted` | Has Papyrus scripts | `VirtualMachineAdapter` |
| `IHaveVirtualMachineAdapter` | Script adapter access | `VirtualMachineAdapter` |
| `IModeled` | Has a 3D model | `Model` |
| `IHasIcons` | Has inventory icons | `Icons` |
| `IEnchantable` | Can be enchanted | `ObjectEffect`, `EnchantmentAmount` |
| `IHarvestable` | Can be harvested | `Ingredient`, `HarvestSound` |
| `IHasEffects` | Has magic effects | `Effects` |
| `IHasDestructible` | Can be destroyed | `Destructible` |
| `IHasVoiceType` | Has voice assignment | `Voice` |
| `IPositionRotation` | Has position/rotation | `Position`, `Rotation` |

---

## Common Subrecords

The `Records/Common Subrecords/` directory contains shared sub-record types used across multiple records:

- **Condition system**: `Condition`, `ConditionFloat`, `ConditionGlobal`, `ConditionData` plus hundreds of game-specific condition data types (e.g., `GetDistanceConditionData`, `HasKeywordConditionData`)
- **VirtualMachineAdapter**: Papyrus script attachment (`AVirtualMachineAdapter`, `ScriptEntry`, `ScriptObjectProperty`)
- **Effect**: Spell/potion effects (`Effect`, `EffectData`)
- **Destructible**: Destruction stages data
- **Attack**: Attack data for NPCs/races
- **BodyTemplate**: Biped object slot assignment
- **ContainerEntry**: Items in containers/inventories
- **Decal**: Decal texture placement data
- **EnableParent**: Enable-state parent references
- **ActivateParents**: Activation parent chain
- **AmbientColors**: Ambient color settings for lighting

---

## Special Group Types

### SkyrimGroup&lt;T&gt;
Standard record group backed by `ICache<T>` for keyed access by FormKey.

### SkyrimListGroup&lt;T&gt;
List-based group for cell blocks (not keyed by FormKey). Used for `Cells` which contains `CellBlock` > `CellSubBlock` > `Cell` hierarchy.

### Cell Block Hierarchy
```
SkyrimMod.Cells (SkyrimListGroup<CellBlock>)
  └─ CellBlock
       └─ SubBlocks (list of CellSubBlock)
            └─ Cells (list of Cell)
                 ├─ Persistent (list of IPlaced)
                 ├─ Temporary (list of IPlaced)
                 ├─ NavigationMeshes
                 └─ Landscape
```

### Worldspace Hierarchy
```
SkyrimMod.Worldspaces (SkyrimGroup<Worldspace>)
  └─ Worldspace
       ├─ TopCell (Cell)
       └─ SubCells (list of WorldspaceBlock)
            └─ Items (list of WorldspaceSubBlock)
                 └─ Items (list of Cell)
```

### Dialog Topic Hierarchy
```
SkyrimMod.DialogTopics (SkyrimGroup<DialogTopic>)
  └─ DialogTopic
       └─ Responses (list of DialogResponses)
```
