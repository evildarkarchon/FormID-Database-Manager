# Mutagen.Bethesda.Starfield

Game-specific record types and mod handling for Starfield. This project contains mostly **generated** record type definitions created by the Loqui code generator, along with handwritten partial classes that provide custom binary parsing, flags, enums, and special behavior. Starfield has significantly more record types than earlier Bethesda games, including many new types for procedural generation, space content, and the object modification system.

**Namespace:** `Mutagen.Bethesda.Starfield`

---

## Generated Code Pattern

Every major record type follows a consistent generated code pattern produced by Loqui (identical to other Mutagen game projects):

### Per-Record Type Artifacts

For a record type named `Foo`, the following are generated:

| Artifact | Description |
|----------|-------------|
| `Foo` | Mutable class inheriting from `StarfieldMajorRecord`. Implements `IFooInternal` and `IEquatable<IFooGetter>`. |
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
- **Aspects**: Records implement shared aspect interfaces (e.g., `INamed`, `IObjectBounded`, `IScripted`).
- **ExtendedList<T>**: Used for lists of sub-records.
- **Object Modification Properties**: Many Starfield records define typed `Property` enums for the object modification (OMOD) system.

### Handwritten Partials

Non-generated `.cs` files contain custom behavior:
- Custom `MajorFlag` and `Flag` enums specific to each record
- `Property` enums for the object modification system (unique to Starfield/Fallout 4+)
- Custom binary parsing/writing for fields that don't follow standard patterns
- Interface implementations (e.g., `IEnchantableGetter`, `IHasVoiceType`)

---

## StarfieldMod Class

The main mod class representing a `.esp`/`.esm`/`.esl` file for Starfield.

**File:** `Records/StarfieldMod.cs` + `Records/StarfieldMod_Generated.cs`

```csharp
public partial class StarfieldMod : AMod, IStarfieldMod
```

### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `ModHeader` | `StarfieldModHeader` | Plugin header (author, description, masters, flags, version) |
| `IsMaster` | `bool` | Whether the mod has the Master flag set |
| `IsSmallMaster` | `bool` | Whether the mod is a light master (.esl) |
| `IsMediumMaster` | `bool` | Whether the mod is a medium master (new in Starfield) |
| `CanBeSmallMaster` | `bool` | Always `true` |
| `CanBeMediumMaster` | `bool` | Always `true` (Starfield supports medium masters) |
| `OverriddenForms` | `IReadOnlyList<IFormLinkGetter<IMajorRecordGetter>>?` | List of forms overridden by this plugin |
| `ListsOverriddenForms` | `bool` | Always `true` |

### Supported Releases

The `StarfieldRelease` enum currently maps to:
- `Starfield` - Starfield (Steam/Xbox)

### Medium Master Support

Unlike Skyrim, Starfield supports **medium masters** in addition to small masters (light plugins). Medium masters use the `StarfieldModHeader.HeaderFlag.Medium` flag and allow a moderate FormID range between full and light masters.

### Builder API

```csharp
// Reading
var mod = StarfieldMod.Create(StarfieldRelease.Starfield)
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

`StarfieldModCommon` contains parallel write methods:
- `WriteCellsParallel` - Parallel serialization of cell blocks/sub-blocks
- `WriteWorldspacesParallel` - Parallel serialization of worldspace hierarchies
- `WriteQuestsParallel` - Parallel serialization of quests (Starfield quests contain embedded dialog topics)

### Custom Record Count Logic

The `GetCustomRecordCount` method tallies groups for cells, worldspaces, and quests. Notably, Starfield's quest record counting includes dialog topics (with responses) as sub-groups under quests, unlike Skyrim where dialog topics are top-level.

---

## Record Group Structure

`StarfieldMod` organizes records into typed groups via `StarfieldGroup<T>`. Starfield has significantly more groups than Skyrim. Groups new to Starfield (not present in Skyrim) are marked with **(NEW)**.

### Core Settings and Keywords

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `GameSettings` | `GameSetting` | Game configuration values (float, int, string, bool, uint) |
| `Keywords` | `Keyword` | Tagging keywords |
| `FormFolderKeywordLists` | `FormFolderKeywordList` | **(NEW)** Keyword lists for folder organization |
| `LocationReferenceTypes` | `LocationReferenceType` | Location reference types |
| `Actions` | `ActionRecord` | Gameplay actions |
| `Transforms` | `Transform` | **(NEW)** Transform definitions |
| `TextureSets` | `TextureSet` | Texture set definitions |
| `Globals` | `Global` | Global variables |
| `DamageTypes` | `DamageType` | **(NEW)** Damage type definitions |
| `Classes` | `Class` | Character classes |
| `DefaultObjectManagers` | `DefaultObjectManager` | Default object assignments |
| `DefaultObjects` | `DefaultObject` | **(NEW)** Individual default objects |

### Characters and NPCs

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `Factions` | `Faction` | Factions |
| `AffinityEvents` | `AffinityEvent` | **(NEW)** Companion affinity events |
| `HeadParts` | `HeadPart` | Character head parts |
| `Races` | `Race` | Races |
| `Npcs` | `Npc` | Non-player characters |
| `LeveledNpcs` | `LeveledNpc` | Leveled NPC lists |
| `VoiceTypes` | `VoiceType` | Voice types |
| `Outfits` | `Outfit` | NPC outfits |

### Audio

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `SoundMarkers` | `SoundMarker` | Sound emitter markers |
| `SoundEchoMarkers` | `SoundEchoMarker` | **(NEW)** Echo sound markers |
| `AcousticSpaces` | `AcousticSpace` | Acoustic environments |
| `AudioOcclusionPrimitives` | `AudioOcclusionPrimitive` | **(NEW)** Audio occlusion shapes |
| `ReverbParameters` | `ReverbParameters` | Reverb settings |
| `MusicTypes` | `MusicType` | Music configurations |
| `MusicTracks` | `MusicTrack` | Music tracks |
| `Footsteps` | `Footstep` | Footstep sounds |
| `FootstepSets` | `FootstepSet` | Footstep sound sets |
| `SoundKeywordMappings` | `SoundKeywordMapping` | **(NEW)** Sound-keyword associations |
| `AnimationSoundTagSets` | `AnimationSoundTagSet` | **(NEW)** Animation sound tags |
| `AmbienceSets` | `AmbienceSet` | **(NEW)** Ambient sound sets |
| `WWiseEventDatas` | `WWiseEventData` | **(NEW)** Wwise audio event data |
| `WWiseKeywordMappings` | `WWiseKeywordMapping` | **(NEW)** Wwise keyword mappings |

### Magic and Effects

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `MagicEffects` | `MagicEffect` | Base magic effects |
| `ObjectEffects` | `ObjectEffect` | Enchantments |
| `Spells` | `Spell` | Spells |
| `Perks` | `Perk` | Perks |
| `EffectShaders` | `EffectShader` | Effect shader visuals |
| `EffectSequences` | `EffectSequence` | **(NEW)** Sequenced effects |
| `Hazards` | `Hazard` | Hazard areas |
| `Explosions` | `Explosion` | Explosions |
| `Projectiles` | `Projectile` | Projectiles |

### Items and Equipment

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `Armors` | `Armor` | Armor items |
| `ArmorAddons` | `ArmorAddon` | Armor visual addons |
| `Books` | `Book` | Books and data slates |
| `Containers` | `Container` | Containers |
| `Doors` | `Door` | Doors |
| `Lights` | `Light` | Light sources |
| `MiscItems` | `MiscItem` | Miscellaneous items |
| `Weapons` | `Weapon` | Weapons |
| `Ammunitions` | `Ammunition` | Ammunition |
| `Keys` | `Key` | Keys |
| `Ingestibles` | `Ingestible` | Consumables |
| `Notes` | `Note` | **(NEW)** Note items (separate from books) |
| `Terminals` | `Terminal` | **(NEW)** Terminal objects |
| `TerminalMenus` | `TerminalMenu` | **(NEW)** Terminal menu content |
| `LeveledItems` | `LeveledItem` | Leveled item lists |
| `LeveledBaseForms` | `LeveledBaseForm` | **(NEW)** Generic leveled base form lists |
| `LeveledPackIns` | `LeveledPackIn` | **(NEW)** Leveled pack-in lists |
| `LeveledSpaceCells` | `LeveledSpaceCell` | **(NEW)** Leveled space cell lists |
| `GenericBaseForms` | `GenericBaseForm` | **(NEW)** Generic base form records |
| `GenericBaseFormTemplates` | `GenericBaseFormTemplate` | **(NEW)** Templates for generic base forms |
| `LegendaryItems` | `LegendaryItem` | **(NEW)** Legendary item definitions |
| `FormLists` | `FormList` | Generic form lists |
| `ConstructibleObjects` | `ConstructibleObject` | Crafting recipes |
| `Resources` | `Resource` | **(NEW)** Crafting/mining resources |
| `ResearchProjects` | `ResearchProject` | **(NEW)** Research project definitions |

### Object Modifications

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `ObjectModifications` | `AObjectModification` | **(NEW)** Object modification system (typed by target) |
| `Zooms` | `Zoom` | **(NEW)** Weapon zoom/scope data |

### World and Cells

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `Worldspaces` | `Worldspace` | World spaces |
| `Cells` | (List group) | Interior cells in blocks/sub-blocks |
| `NavigationMeshInfoMaps` | `NavigationMeshInfoMap` | NavMesh metadata |
| `NavigationMeshObstacleCoverManagers` | `NavigationMeshObstacleCoverManager` | **(NEW)** NavMesh obstacle/cover data |
| `Locations` | `Location` | Location records |
| `Regions` | `Region` | Regions |
| `LandscapeTextures` | `LandscapeTexture` | Terrain textures |
| `Layers` | `Layer` | **(NEW)** Layer definitions |
| `PackIns` | `PackIn` | **(NEW)** Prefab pack-in assemblies |
| `StaticCollections` | `StaticCollection` | **(NEW)** Static mesh collections |
| `ReferenceGroups` | `ReferenceGroup` | **(NEW)** Reference groupings |
| `Messages` | `Message` | Message boxes |
| `LoadScreens` | `LoadScreen` | Loading screens |
| `LightingTemplates` | `LightingTemplate` | Lighting presets |

### Space and Planets (NEW to Starfield)

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `Planets` | `Planet` | **(NEW)** Planet/moon/star definitions |
| `Stars` | `Star` | **(NEW)** Star definitions |
| `Biomes` | `Biome` | **(NEW)** Planet biome types |
| `BiomeMarkers` | `BiomeMarker` | **(NEW)** Biome placement markers |
| `Atmospheres` | `Atmosphere` | **(NEW)** Atmospheric settings |
| `SunPresets` | `SunPreset` | **(NEW)** Sun rendering presets |
| `PlanetContentManagerTrees` | `PlanetContentManagerTree` | **(NEW)** Planet content generation trees |
| `PlanetContentManagerBranchNodes` | `PlanetContentManagerBranchNode` | **(NEW)** Content tree branch nodes |
| `PlanetContentManagerContentNodes` | `PlanetContentManagerContentNode` | **(NEW)** Content tree leaf nodes |
| `SurfaceBlocks` | `SurfaceBlock` | **(NEW)** Surface block definitions |
| `SurfacePatterns` | `SurfacePattern` | **(NEW)** Surface pattern definitions |
| `SurfacePatternConfigs` | `SurfacePatternConfig` | **(NEW)** Surface pattern configurations |
| `SurfacePatternStyles` | `SurfacePatternStyle` | **(NEW)** Surface pattern visual styles |
| `SurfaceTrees` | `SurfaceTree` | **(NEW)** Surface tree placement |
| `ResourceGenerationData` | `ResourceGenerationData` | **(NEW)** Resource generation parameters |
| `GroundCovers` | `GroundCover` | **(NEW)** Ground cover definitions |

### Quests and Dialog

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `Quests` | `Quest` | Quest definitions (contain dialog topics in Starfield) |
| `IdleAnimations` | `IdleAnimation` | Idle animation definitions |
| `Packages` | `Package` | AI packages |
| `CombatStyles` | `CombatStyle` | Combat behavior |
| `StoryManagerBranchNodes` | `StoryManagerBranchNode` | Story manager branches |
| `StoryManagerQuestNodes` | `StoryManagerQuestNode` | Story manager quests |
| `StoryManagerEventNodes` | `StoryManagerEventNode` | Story manager events |
| `SceneCollections` | `SceneCollection` | **(NEW)** Scene collections |
| `SpeechChallenges` | `SpeechChallenge` | **(NEW)** Speech challenge records |
| `Challenges` | `Challenge` | **(NEW)** Challenge records |

### Statics and World Objects

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `Statics` | `Static` | Static meshes |
| `MoveableStatics` | `MoveableStatic` | Physics statics |
| `Activators` | `Activator` | Activatable objects |
| `Grasses` | `Grass` | Grass definitions |
| `Florae` | `Flora` | Harvestable plants |
| `Furniture` | `Furniture` | Furniture/markers |
| `AnimatedObjects` | `AnimatedObject` | Animated objects |
| `AddonNodes` | `AddonNode` | Add-on nodes |
| `ArtObjects` | `ArtObject` | Art objects |
| `IdleMarkers` | `IdleMarker` | Idle markers |
| `BendableSplines` | `BendableSpline` | **(NEW)** Bendable spline objects |
| `ProjectedDecals` | `ProjectedDecal` | **(NEW)** Projected decal definitions |

### Visual and Weather

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `Weathers` | `Weather` | Weather definitions |
| `WeatherSettings` | `WeatherSetting` | **(NEW)** Weather setting configurations |
| `Climates` | `Climate` | Climate configurations |
| `Clouds` | `Clouds` | **(NEW)** Cloud definitions |
| `FogVolumes` | `FogVolume` | **(NEW)** Volumetric fog |
| `ShaderParticleGeometries` | `ShaderParticleGeometry` | Shader particles |
| `Waters` | `Water` | Water types |
| `ImageSpaces` | `ImageSpace` | Post-processing |
| `ImageSpaceAdapters` | `ImageSpaceAdapter` | Animated post-processing |
| `VolumetricLightings` | `VolumetricLighting` | Volumetric lighting |
| `LensFlares` | `LensFlare` | Lens flare effects |
| `Debris` | `Debris` | Debris collections |

### Physics and Combat

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `Impacts` | `Impact` | Impact effects |
| `ImpactDataSets` | `ImpactDataSet` | Impact effect sets |
| `MaterialTypes` | `MaterialType` | Material types |
| `CollisionLayers` | `CollisionLayer` | Collision layers |
| `BodyParts` | `BodyPartData` | Body part hit data |
| `ActorValueInformation` | `ActorValueInformation` | Actor values |
| `ActorValueModulations` | `ActorValueModulation` | **(NEW)** Actor value modulations |
| `AimModels` | `AimModel` | **(NEW)** Aim model parameters |
| `AimAssistModels` | `AimAssistModel` | **(NEW)** Aim assist configurations |
| `AimAssistPoses` | `AimAssistPose` | **(NEW)** Aim assist pose data |
| `AimOpticalSightMarkers` | `AimOpticalSightMarker` | **(NEW)** Optical sight markers |
| `MeleeAimAssistModels` | `MeleeAimAssistModel` | **(NEW)** Melee aim assist |
| `ForceDatas` | `ForceData` | **(NEW)** Force/physics data |
| `SecondaryDamageLists` | `SecondaryDamageList` | **(NEW)** Secondary damage definitions |
| `WeaponBarrelModels` | `WeaponBarrelModel` | **(NEW)** Weapon barrel definitions |

### Cameras

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `CameraShots` | `CameraShot` | Camera shots |
| `CameraPaths` | `CameraPath` | Camera paths |

### Snap Templates (NEW to Starfield)

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `SnapTemplates` | `SnapTemplate` | **(NEW)** Snap-together building templates |
| `SnapTemplateNodes` | `SnapTemplateNode` | **(NEW)** Snap template connection nodes |
| `SnapTemplateBehaviors` | `SnapTemplateBehavior` | **(NEW)** Snap template behavior rules |

### Miscellaneous

| Group Property | Record Type | Description |
|----------------|-------------|-------------|
| `CurveTables` | `CurveTable` | **(NEW)** Curve table data |
| `Curve3Ds` | `Curve3D` | **(NEW)** 3D curve data |
| `Colors` | `ColorRecord` | Color definitions |
| `EquipTypes` | `EquipType` | Equip slot types |
| `MovementTypes` | `MovementType` | Movement types |
| `ObjectSwaps` | `ObjectSwap` | **(NEW)** Object swap definitions |
| `ObjectVisibilityManagers` | `ObjectVisibilityManager` | **(NEW)** Object visibility control |
| `AttractionRules` | `AttractionRule` | **(NEW)** Attraction rules |
| `Traversals` | `Traversal` | **(NEW)** Traversal records |
| `MorphableObjects` | `MorphableObject` | **(NEW)** Morphable object definitions |
| `BoneModifiers` | `BoneModifier` | **(NEW)** Bone modifier definitions |
| `ConditionRecords` | `ConditionRecord` | **(NEW)** Standalone condition records |
| `MaterialPaths` | `MaterialPath` | **(NEW)** Material path definitions |
| `LayeredMaterialSwaps` | `LayeredMaterialSwap` | **(NEW)** Layered material swap definitions |
| `ParticleSystemDefineCollisions` | `ParticleSystemDefineCollision` | **(NEW)** Particle collision definitions |
| `PhotoModeFeatures` | `PhotoModeFeature` | **(NEW)** Photo mode features |
| `GameplayOptions` | `GameplayOption` | **(NEW)** Gameplay option definitions |
| `GameplayOptionsGroups` | `GameplayOptionsGroup` | **(NEW)** Gameplay option groups |
| `TimeOfDays` | `TimeOfDayRecord` | **(NEW)** Time of day records |
| `FacialExpressions` | `FacialExpression` | **(NEW)** Facial expression records |
| `PERS` | `PERS` | **(NEW)** Unknown/persistence records |

---

## Major Record Type Catalog

### Characters and Creatures

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Npc` | Non-player characters | `Race`, `Name`, `Flags`, `Level`, `Factions`, `Packages`, `Items`, `Class`, `Voice`, `Properties` | `INpcGetter` |
| `Race` | Playable and creature races | `Name`, `Flags`, `Skin`, `Spells`, `HeadData`, `BodyData`, `MovementTypes` | `IRaceGetter` |
| `Class` | Character classes | `Name`, `Teaches`, `MaxTraining`, `ActorValues` | `IClassGetter` |
| `Faction` | Factions | `Name`, `Flags`, `Relations`, `Ranks`, `CrimeValues`, `VendorValues` | `IFactionGetter` |
| `AffinityEvent` | Companion affinity events | `Name`, `ReactionKeywords` | `IAffinityEventGetter` |
| `VoiceType` | Voice types | `Flags` | `IVoiceTypeGetter` |
| `LeveledNpc` | Leveled NPC lists | `Entries`, `ChanceNone`, `Flags` | `ILeveledNpcGetter` |

### Weapons (Extensive Modification Support)

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Weapon` | Weapons | `Name`, `ObjectBounds`, `ObjectEffect`, `DamagePhysical`, `FiringType`, `AmmoType`, `ShotsPerSecond`, `AimModel`, `Keywords` | `IWeaponGetter` |
| `WeaponBarrelModel` | Barrel models | Barrel-specific parameters | `IWeaponBarrelModelGetter` |
| `Zoom` | Scope/zoom settings | Zoom parameters | `IZoomGetter` |
| `AimModel` | Aim model parameters | Cone, recoil, stability settings | `IAimModelGetter` |
| `AimAssistModel` | Aim assist settings | Cone angle, steering, snap settings | `IAimAssistModelGetter` |
| `AimAssistPose` | Aim assist poses | Pose point configurations | `IAimAssistPoseGetter` |
| `AimOpticalSightMarker` | Optical sights | Sight marker data | `IAimOpticalSightMarkerGetter` |
| `MeleeAimAssistModel` | Melee aim assist | Melee-specific aim assist | `IMeleeAimAssistModelGetter` |

### Object Modification System

The object modification system (`AObjectModification`) is a major Starfield feature allowing runtime property overrides on records.

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `AObjectModification` | Abstract base for all OMODs | `AttachPoint`, `AttachParentSlots`, `Includes`, `Properties` | `IAObjectModificationGetter` |
| `WeaponModification` | Weapon modifications | `Properties` (typed as `Weapon.Property`) | `IWeaponModificationGetter` |
| `ArmorModification` | Armor modifications | `Properties` (typed as `Armor.Property`) | `IArmorModificationGetter` |
| `NpcModification` | NPC modifications | `Properties` (typed as `Npc.Property`) | `INpcModificationGetter` |
| `FloraModification` | Flora modifications | `Properties` (typed as `Flora.Property`) | `IFloraModificationGetter` |
| `ObjectModification` | Generic modifications | `Properties` (typed as `NoneProperty`) | `IObjectModificationGetter` |
| `UnknownObjectModification` | Unknown target types | `Properties`, `ObjectModificationTargetName` | `IUnknownObjectModificationGetter` |

The `AObjectModification.CreateFromBinary` factory dispatches to the correct typed modification based on the `DATA` subrecord's embedded target name string (e.g., `"TESObjectWEAP_InstanceData"`, `"TESObjectARMO_InstanceData"`).

Each modification target has a comprehensive `Property` enum. For example, `Weapon.Property` includes over 100 properties covering damage, aim, recoil, firing type, ammo, scope, sound, and more.

### Items and Equipment

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Armor` | Armor items | `Name`, `ObjectBounds`, `ObjectEffect`, `Value`, `Weight`, `Keywords` | `IArmorGetter` |
| `ArmorAddon` | Armor visual addons | `WorldModel`, `FirstPersonModel`, `Race`, `AdditionalRaces` | `IArmorAddonGetter` |
| `Ammunition` | Ammunition | `Name`, `ObjectBounds`, `Projectile`, `Damage`, `Value`, `Flags` | `IAmmunitionGetter` |
| `Book` | Books and data slates | `Name`, `Text`, `Value`, `Weight`, `Flags` | `IBookGetter` |
| `Note` | Notes (separate type) | `Name`, `Type`, `Data` | `INoteGetter` |
| `Ingestible` | Consumables | `Name`, `Value`, `Weight`, `Effects`, `Flags` | `IIngestibleGetter` |
| `MiscItem` | Miscellaneous items | `Name`, `Value`, `Weight`, `Keywords` | `IMiscItemGetter` |
| `Key` | Key items | `Name`, `Value`, `Weight` | `IKeyGetter` |
| `Container` | Containers | `Name`, `Items`, `Flags`, `Weight` | `IContainerGetter` |
| `Terminal` | Terminal objects | `Name`, `BackgroundType` | `ITerminalGetter` |
| `TerminalMenu` | Terminal menus | Menu items and configuration | `ITerminalMenuGetter` |
| `LeveledItem` | Leveled item lists | `Entries`, `ChanceNone`, `Flags` | `ILeveledItemGetter` |
| `LeveledBaseForm` | Generic leveled lists | `Entries`, `ChanceNone`, `Flags` | `ILeveledBaseFormGetter` |
| `LeveledPackIn` | Leveled pack-ins | `Entries`, `Flags` | `ILeveledPackInGetter` |
| `LeveledSpaceCell` | Leveled space cells | `Entries`, `Flags` | `ILeveledSpaceCellGetter` |
| `GenericBaseForm` | Generic base forms | Base form data | `IGenericBaseFormGetter` |
| `GenericBaseFormTemplate` | Form templates | Template definitions | `IGenericBaseFormTemplateGetter` |
| `LegendaryItem` | Legendary items | Legendary rules and effects | `ILegendaryItemGetter` |
| `Resource` | Crafting resources | `Name`, resource data | `IResourceGetter` |
| `ResearchProject` | Research projects | `Tier`, requirements | `IResearchProjectGetter` |
| `ConstructibleObject` | Crafting recipes | `Items`, `Conditions`, `CreatedObject`, `WorkbenchKeyword` | `IConstructibleObjectGetter` |
| `FormList` | Generic form lists | `Items` | `IFormListGetter` |
| `Outfit` | NPC outfits | `Items` | `IOutfitGetter` |

### Space and Planets

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Planet` | Planets, moons, stations | `BodyType`, `Biomes`, `Resources`, `Gravity`, `Atmosphere`, `PlayerKnowledgeFlags` | `IPlanetGetter` |
| `Star` | Star definitions | Star properties | `IStarGetter` |
| `Biome` | Planet biome types | `Type` (Default/Ocean/Polar/Mountain/Swamp/Archipelago/GasGiant), `TerrainMask` | `IBiomeGetter` |
| `BiomeMarker` | Biome markers | Placement data | `IBiomeMarkerGetter` |
| `Atmosphere` | Atmospheric settings | Atmospheric properties | `IAtmosphereGetter` |
| `SunPreset` | Sun rendering | Sun parameters | `ISunPresetGetter` |
| `PlanetContentManagerTree` | Content generation trees | Tree structure for procedural content | `IPlanetContentManagerTreeGetter` |
| `PlanetContentManagerBranchNode` | Content tree branches | Branch logic | `IPlanetContentManagerBranchNodeGetter` |
| `PlanetContentManagerContentNode` | Content tree leaves | Actual content data | `IPlanetContentManagerContentNodeGetter` |

### Surface Generation

| Record Type | Description | Getter Interface |
|-------------|-------------|-----------------|
| `SurfaceBlock` | Surface block definitions | `ISurfaceBlockGetter` |
| `SurfacePattern` | Surface pattern definitions | `ISurfacePatternGetter` |
| `SurfacePatternConfig` | Pattern configurations | `ISurfacePatternConfigGetter` |
| `SurfacePatternStyle` | Pattern visual styles | `ISurfacePatternStyleGetter` |
| `SurfaceTree` | Surface tree placement | `ISurfaceTreeGetter` |
| `GroundCover` | Ground cover definitions | `IGroundCoverGetter` |
| `ResourceGenerationData` | Resource gen parameters | `IResourceGenerationDataGetter` |

### World and Cells

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Worldspace` | World spaces | `Name`, `TopCell`, `SubCells`, `Music`, `Climate`, `Water`, `Flags` | `IWorldspaceGetter` |
| `Cell` | Interior/exterior cells | `Flags`, `Lighting`, `Water`, `Owner`, `Persistent`, `Temporary`, `NavigationMeshes` | `ICellGetter` |
| `NavigationMesh` | Pathfinding mesh | `Data` | `INavigationMeshGetter` |
| `NavigationMeshInfoMap` | NavMesh metadata | `NavMeshInfos` | `INavigationMeshInfoMapGetter` |
| `NavigationMeshObstacleCoverManager` | NavMesh obstacles/cover | Obstacle/cover data | `INavigationMeshObstacleCoverManagerGetter` |
| `PlacedObject` | Placed references (REFR) | `Base`, `Position`, `Rotation`, `Scale`, `EnableParent` | `IPlacedObjectGetter` |
| `PlacedNpc` | Placed NPCs (ACHR) | `Base`, `Position`, `Rotation`, `Scale` | `IPlacedNpcGetter` |
| `Location` | Location data | `Name`, `Keywords`, `ParentLocation`, `Factions` | `ILocationGetter` |
| `Region` | World regions | `MapName`, `Data` | `IRegionGetter` |
| `PackIn` | Pack-in prefabs | Contents, `MajorFlag.Prefab` | `IPackInGetter` |
| `Layer` | Layer definitions | Layer data | `ILayerGetter` |

### Snap Templates (Building System)

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `SnapTemplate` | Building snap templates | Template geometry and connection rules | `ISnapTemplateGetter` |
| `SnapTemplateNode` | Snap connection nodes | Node position and orientation | `ISnapTemplateNodeGetter` |
| `SnapTemplateBehavior` | Snap behavior rules | Behavior conditions and effects | `ISnapTemplateBehaviorGetter` |

### Quests and Dialog

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `Quest` | Quest definitions | `Name`, `Flags`, `Stages`, `Objectives`, `Aliases`, `DialogTopics` | `IQuestGetter` |
| `DialogTopic` | Dialog topics | `Name`, `Branch`, `TopicFlags`, `Responses` | `IDialogTopicGetter` |
| `DialogBranch` | Dialog branches | `Quest`, `Category`, `Flags`, `StartingTopic` | `IDialogBranchGetter` |
| `DialogResponses` | Dialog responses | `Conditions`, `Responses`, `Script`, `Flags` | `IDialogResponsesGetter` |
| `Scene` | Scripted scenes | `Phases`, `Actors`, `Actions`, `Quest` | `ISceneGetter` |
| `SceneCollection` | Scene collections | Scene groupings | `ISceneCollectionGetter` |
| `Package` | AI packages | `Flags`, `Type`, `Conditions`, `Data` | `IPackageGetter` |
| `SpeechChallenge` | Speech challenges | Challenge parameters | `ISpeechChallengeGetter` |
| `Challenge` | Challenges | Challenge definitions | `IChallengeGetter` |

### Game Configuration

| Record Type | Description | Key Fields | Getter Interface |
|-------------|-------------|------------|-----------------|
| `GameSetting` | Game settings (abstract) | `EditorID`, `SettingType` | `IGameSettingGetter` |
| `GameSettingFloat` | Float setting | `Data` | `IGameSettingFloatGetter` |
| `GameSettingInt` | Integer setting | `Data` | `IGameSettingIntGetter` |
| `GameSettingString` | String setting | `Data` | `IGameSettingStringGetter` |
| `GameSettingBool` | Boolean setting | `Data` | `IGameSettingBoolGetter` |
| `GameSettingUInt` | Unsigned int setting **(NEW)** | `Data` | `IGameSettingUIntGetter` |
| `Global` | Global variables | `RawFloat` | `IGlobalGetter` |
| `Keyword` | Keywords | `Name`, `Color`, `Type` | `IKeywordGetter` |
| `DefaultObjectManager` | Default objects | `Objects` | `IDefaultObjectManagerGetter` |
| `DefaultObject` | Individual defaults | Default object reference | `IDefaultObjectGetter` |
| `GameplayOption` | Gameplay options | Option definitions | `IGameplayOptionGetter` |
| `GameplayOptionsGroup` | Option groups | Grouped options | `IGameplayOptionsGroupGetter` |
| `ConditionRecord` | Standalone conditions | Condition data | `IConditionRecordGetter` |

---

## Handwritten Partials (Notable Custom Behavior)

### Npc (`Npc.cs`)
- `MajorFlag.BleedoutOverride` flag
- Extensive `Property` enum (26 properties) for the object modification system: ActorValue, NpcRaceOverride, AIData, CombatStyle, Enchantment, Faction, Inventory, Package, VoiceType, Keyword, Spell, Perk, and more
- `Flag` enum with 20+ flags (Female, Essential, Respawn, Unique, Protected, Invulnerable, etc.)
- `AggressionType`, `ConfidenceType`, `ResponsibilityType`, `MoodType`, `AssistanceType` behavior enums
- `TemplateActorType` flags for template inheritance (Traits, Stats, Factions, SpellList, etc.)
- Custom binary handling for PcLevelMult flag in the Level field (level mult stored as uint16 * 1000)
- `IHasVoiceTypeGetter` implementation
- `ObjectModificationName = "TESNPC_InstanceData"` for OMOD targeting

### Weapon (`Weapon.cs`)
- `MajorFlag` (NonPlayable, HighResFirstPersonOnly)
- `FiringTypeEnum` (SingleOrBinary, Burst, Automatic)
- `ObjectModificationName = "TESObjectWEAP_InstanceData"`
- Massive `Property` enum with **100+ properties** covering:
  - Aim assist (inner/outer cone, steering, snap, ADS multipliers)
  - Ammo (type, capacity, NPCsUseAmmo)
  - Damage (physical, damage types, critical damage/chance)
  - Firing (rate, burst count/delay, full auto, bolt action, staged trigger)
  - Aim model (stability, cone min/max, recoil, iron sights multiplier)
  - Range (min/max, out-of-range damage mult)
  - Scope/zoom (ADS image space, FOV mult, offset X/Y/Z, overlay)
  - Variable range aperture/distance parameters
  - Sound, reload, enchantment, keywords, and more

### AObjectModification (`ObjectModification.cs`)
- Abstract polymorphic factory: `AObjectModification.CreateFromBinary` dispatches to `WeaponModification`, `ArmorModification`, `NpcModification`, `FloraModification`, `ObjectModification`, or `UnknownObjectModification` based on DATA subrecord name
- `MajorFlag` (LegendaryMod, ModCollection)
- Internal `DeletedObjectModification` for handling deleted OMOD records
- Complex binary parsing for includes, properties, attach points, and attach parent slots
- Binary overlay implementations for all modification subtypes

### Planet (`Planet.cs`)
- `BodyTypeEnum` (Undefined, Star, Planet, Moon, Orbital, AsteroidBelt, Station)
- `PlayerKnowledgeFlag` flags (InitialScan, Visited, EnteredSystem)

### Biome (`Biome.cs`)
- `TypeEnum` (Default, Ocean, Polar, Mountain, Swamp, Archipelago, GasGiant)
- `TerrainMask` (Base, Solid, FlatOuter, FlatInner, Talus, Flow, Path)

### ResearchProject (`ResearchProject.cs`)
- `TierEnum` (Tier0 through Tier5)

### Terminal (`Terminal.cs`)
- `MajorFlag` (HasDistantLod, RandomAnimStart)
- `BackgroundType` enum with Starfield faction themes (Constellation, FreestarCollective, Default, NASA, RyujinIndustries, SlaytonAerospace, UnitedColonies, CrimsonFleet)

### LeveledBaseForm (`LeveledBaseForm.cs`)
- `MajorFlag.CalculateAll`
- `Flag` enum including `ContainsOnlySpaceshipBaseForms` for spaceship leveled lists

### PackIn (`PackIn.cs`)
- `MajorFlag.Prefab` flag for marking prefab assemblies

### Cell (`Cell.cs`) (Starfield-specific `Cell.cs` is in the Starfield project)
- Inherits the same complex cell children group handling as Skyrim
- Supports persistent/temporary children with placed objects, NPCs, and various trap types

### GameSetting (`GameSetting.cs`)
- Same factory pattern as Skyrim but adds `GameSettingUInt` for unsigned integer settings

---

## Key Enums

### BipedObjectFlag (Starfield-specific body slots)
```
HairTop, HairLong, FaceGenHead, Body, LeftHand, RightHand,
TorsoUnderArmor, LeftArmUnderArmor, RightArmUnderArmor,
LeftLegUnderArmor, RightLegUnderArmor, TorsoArmor, LeftArmArmor,
RightArmArmor, LeftLegArmor, RightLegArmor, Headband, Eyes, Beard,
Mouth, Neck, Ring, Scalp, Decapitation, Shield, Pipboy, FX
```
Note: Significantly different from Skyrim's simpler biped slots (Head, Hair, Body, etc.).

### ActorValue
Comprehensive enum covering all Starfield actor values including health, skills, resistances, O2 (oxygen), CO2, and Starfield-specific values.

### Other Important Enums
- `CastType` - ConstantEffect, FireAndForget, Concentration
- `TargetType` - Self, Touch, Aimed, TargetActor, TargetLocation
- `SoundLevel` - Loud, Normal, Silent, VeryLoud
- `LockLevel` - Novice through Inaccessible
- `Level` - Easy, Medium, Hard, VeryHard
- `Size` - Small through ExtraLarge
- `Stagger` - None through ExtraLarge
- `HitBehavior` - Normal, Dismember, Explode, NoDismemberOrExplode
- `Pronoun` - HeHim, SheHer, TheyThem (new to Starfield)
- `PerkCategory` / `PerkSkillGroup` - Perk organization
- `Perspective` - FirstPerson, ThirdPerson
- `TintType` - SkinTone, HairColor, EyeColor, etc.
- `GroupTypeEnum` - Record group types (Top through CellTemporaryChildren)
- `FurnitureMarkerFlags` - Furniture usage flags
- `FirstPersonFlag` - First-person model flags

---

## Link Interfaces

Link interfaces in `Interfaces/Link/` define polymorphic groupings (same structure as Skyrim but with additional types):

| Interface | Description | Notable Implementors |
|-----------|-------------|---------------------|
| `IItem` | Any inventory item | `Weapon`, `Armor`, `MiscItem`, `Ingestible`, `Book`, `Key`, `Note`, etc. |
| `IPlaced` | Any placed reference | `PlacedObject`, `PlacedNpc`, `PlacedArrow`, `PlacedTrap`, etc. |
| `INpcSpawn` | NPC or leveled NPC | `Npc`, `LeveledNpc` |
| `IConstructible` | Craftable items | Items usable in crafting |
| `IReferenceableObject` | Objects that can be placed | Items, statics, and other placeable types |

---

## Aspect Interfaces

Same as Skyrim's aspect system:

| Interface | Description |
|-----------|-------------|
| `INamed` | Has a name property |
| `IObjectBounded` | Has 3D bounds |
| `IScripted` | Has Papyrus scripts |
| `IHaveVirtualMachineAdapter` | Script adapter access |
| `IModeled` | Has a 3D model |
| `IEnchantable` | Can be enchanted |
| `IHasVoiceType` | Has voice assignment |

---

## Special Group Types

### StarfieldGroup&lt;T&gt;
Standard record group backed by `ICache<T>` for keyed access by FormKey.

### StarfieldListGroup&lt;T&gt;
List-based group for cell blocks (not keyed by FormKey).

### Cell Block Hierarchy
```
StarfieldMod.Cells (StarfieldListGroup<CellBlock>)
  +-- CellBlock
       +-- SubBlocks (list of CellSubBlock)
            +-- Cells (list of Cell)
                 +-- Persistent (list of IPlaced)
                 +-- Temporary (list of IPlaced)
                 +-- NavigationMeshes
```
Note: Starfield cells do not have a `Landscape` field (unlike Skyrim).

### Worldspace Hierarchy
```
StarfieldMod.Worldspaces (StarfieldGroup<Worldspace>)
  +-- Worldspace
       +-- TopCell (Cell)
       +-- SubCells (list of WorldspaceBlock)
            +-- Items (list of WorldspaceSubBlock)
                 +-- Items (list of Cell)
```

### Quest-Dialog Hierarchy (Different from Skyrim)
In Starfield, dialog topics are nested under quests rather than being top-level:
```
StarfieldMod.Quests (StarfieldGroup<Quest>)
  +-- Quest
       +-- DialogTopics (list of DialogTopic)
            +-- Responses (list of DialogResponses)
```

---

## Differences from Skyrim

Key architectural and content differences from Mutagen.Bethesda.Skyrim:

1. **Medium Master Support**: Starfield supports medium masters (`CanBeMediumMaster = true`).
2. **No Small Master "Light" Naming**: Uses `HeaderFlag.Light` instead of `HeaderFlag.Small`.
3. **Object Modification System**: Comprehensive OMOD system with typed property enums per target record type.
4. **Dialog Under Quests**: Dialog topics are children of quests, not top-level groups.
5. **No Landscape in Cells**: Starfield cells lack the `Landscape` field present in Skyrim.
6. **No SoundDescriptor/SoundOutputModel/SoundCategory**: Starfield uses Wwise audio middleware instead.
7. **No Scroll/Shout/WordOfPower/DualCastData**: These Skyrim-specific magic types don't exist.
8. **No TalkingActivator/Ingredient/SoulGem/AlchemicalApparatus**: Different item system.
9. **No EncounterZone**: Different encounter scaling.
10. **GameSettingUInt**: Starfield adds unsigned integer game settings.
11. **Pronoun Enum**: Character pronoun support (HeHim, SheHer, TheyThem).
12. **Extensive New Record Types**: ~70+ new record types for space, planets, surfaces, snap templates, aim systems, and more.
13. **BipedObject Slots**: Completely different body slot system with under-armor layers.
14. **Pack-Ins**: Prefab assembly system for modular building.
