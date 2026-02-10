# Mutagen.Bethesda.Core API Reference

The core infrastructure library for Mutagen, providing foundational types for working with Bethesda game plugins. This project contains archives, plugins, strings, translations, environments, game installation detection, Papyrus (Pex) script parsing, and dependency injection modules.

**Namespace root**: `Mutagen.Bethesda`

---

## Table of Contents

- [Mutagen.Bethesda.Plugins (Core Types)](#mutagenbethesdaplugins-core-types)
  - [FormKey](#formkey)
  - [FormID](#formid)
  - [RecordType](#recordtype)
  - [FormLink / IFormLink](#formlink--iformlink)
  - [FormLinkInformation](#formlinkinformation)
  - [ILink](#ilink)
- [Mutagen.Bethesda.Plugins.Records](#mutagenbethesdapluginsrecords)
  - [IMod / IModGetter](#imod--imodgetter)
  - [IMajorRecord / IMajorRecordGetter](#imajorrecord--imajorrecordgetter)
  - [IGroup / IGroupGetter](#igroup--igroupgetter)
  - [IModHeaderCommon](#imodheadercommon)
  - [ModFactory](#modfactory)
  - [GenderedItem](#gendereditem)
  - [ModFlags / IModFlagsGetter](#modflags--imodflagsgetter)
  - [IMajorRecordIdentifier](#imajorrecordidentifier)
- [Mutagen.Bethesda.Plugins.Aspects](#mutagenbethesdapluginsaspects)
  - [INamed / INamedGetter](#inamed--inamedgetter)
  - [ITranslatedNamed / ITranslatedNamedGetter](#itranslatednamed--itranslatednamedgetter)
  - [IKeyworded / IKeywordedGetter](#ikeyworded--ikeywordedgetter)
  - [IWeightValue / IWeightValueGetter](#iweightvalue--iweightvaluegetter)
- [Mutagen.Bethesda.Plugins.Cache](#mutagenbethesdapluginscache)
  - [ILinkCache](#ilinkcache)
  - [IModContext](#imodcontext)
  - [ResolveTarget](#resolvetarget)
  - [LinkCachePreferences](#linkcachepreferences)
- [Mutagen.Bethesda.Plugins.Order](#mutagenbethesdapluginsorder)
  - [LoadOrder](#loadorder)
  - [LoadOrderListing / ILoadOrderListingGetter](#loadorderlisting--iloadorderlistinggetter)
  - [ModListing / IModListingGetter](#modlisting--imodlistinggetter)
- [Mutagen.Bethesda.Plugins.Allocators](#mutagenbethesdapluginsallocators)
  - [IFormKeyAllocator](#iformkeyallocator)
- [Mutagen.Bethesda.Plugins.Meta](#mutagenbethesdapluginsmeta)
  - [GameConstants](#gameconstants)
- [Mutagen.Bethesda.Plugins.Masters](#mutagenbethesdapluginsmasters)
  - [MasterStyle](#masterstyle)
- [Mutagen.Bethesda.Plugins.Binary](#mutagenbethesdapluginsbinary)
  - [Headers](#headers)
  - [Binary Parameters](#binary-parameters)
  - [Streams](#streams)
- [Mutagen.Bethesda.Plugins.Assets](#mutagenbethesdapluginsassets)
  - [IAssetLink / AssetLink](#iassetlink--assetlink)
- [Mutagen.Bethesda.Archives](#mutagenbethesdaarchives)
  - [Archive](#archive)
  - [IArchiveReader](#iarchivereader)
  - [IArchiveFile](#iarchivefile)
  - [IArchiveFolder](#iarchivefolder)
- [Mutagen.Bethesda.Environments](#mutagenbethesdaenvironments)
  - [GameEnvironment](#gameenvironment)
  - [IGameEnvironment](#igameenvironment)
  - [GameEnvironmentState](#gameenvironmentstate)
  - [GameEnvironmentBuilder](#gameenvironmentbuilder)
- [Mutagen.Bethesda.Installs](#mutagenbethesdainstalls)
  - [GameLocations](#gamelocations)
- [Mutagen.Bethesda.Strings](#mutagenbethesdastrings)
  - [Language](#language)
  - [ITranslatedString / TranslatedString](#itranslatedstring--translatedstring)
- [Mutagen.Bethesda.Assets](#mutagenbethesdaassets)
  - [IAssetType](#iassettype)
  - [DataRelativePath](#datarelativepath)
- [Mutagen.Bethesda.Pex](#mutagenbethesdapex)
  - [PexFile](#pexfile)
  - [Pex Enums](#pex-enums)

---

## Mutagen.Bethesda.Plugins (Core Types)

### FormKey

A struct representing a unique identifier for a record, combining a record ID (6 bytes) with the `ModKey` the record originates from. FormKeys are preferred over FormIDs because they cannot be misinterpreted depending on load order context and remove the 255-master limit while in code space.

**Namespace**: `Mutagen.Bethesda.Plugins`

```csharp
public readonly struct FormKey : IEquatable<FormKey>, IComparable<FormKey>, IFormKeyGetter
{
    // Static singletons
    public static readonly FormKey Null;          // 00000000
    public static readonly FormKey None;          // FFFFFFFF
    public const string NullStr = "Null";
    public const string NoneStr = "None";

    // Fields
    public readonly uint ID;                      // Record ID (lower 6 bytes only)
    public readonly ModKey ModKey;                // Originating mod

    // Properties
    public bool IsNull { get; }                   // True if ModKey is null

    // Constructors
    public FormKey(ModKey modKey, uint id);

    // Factory methods (string format: "012ABC:ModName.esp")
    public static bool TryFactory(ReadOnlySpan<char> str, out FormKey formKey);
    public static FormKey? TryFactory(ReadOnlySpan<char> str);
    public static FormKey Factory(ReadOnlySpan<char> str);

    // Conversion methods
    public string IDString();                     // Returns "FFFFFF" hex format
    public string ToFilesafeString();             // Returns "FFFFFF_ModName.esp"
    public FormLink<TMajorGetter> ToLink<TMajorGetter>()
        where TMajorGetter : class, IMajorRecordGetter;
    public IFormLinkGetter<TMajorGetter> ToLinkGetter<TMajorGetter>()
        where TMajorGetter : class, IMajorRecordGetter;

    // Comparers
    public static Comparer<FormKey> AlphabeticalComparer(bool mastersFirst = true);
    public static Comparer<FormKey> LoadOrderComparer(
        IReadOnlyList<ModKey> loadOrder,
        Comparer<FormKey>? matchingModKeyFallback = null,
        Comparer<FormKey>? notOnLoadOrderFallback = null);
    public static Comparer<FormKey> LoadOrderComparer(
        ILoadOrderGetter loadOrder, ...);
    public static Comparer<FormKey> LoadOrderComparer<TItem>(
        LoadOrder<TItem> loadOrder, ...)
        where TItem : class, IModKeyed;
}
```

---

### FormID

A struct wrapping a raw `uint` representing a FormID as stored on disk. Supports Full, Medium (0xFD prefix), and Small/Light (0xFE prefix) master styles. FormID should be used sparingly -- prefer `FormKey` for in-code use.

**Namespace**: `Mutagen.Bethesda.Plugins`

```csharp
public readonly struct FormID : IEquatable<FormID>
{
    public static readonly FormID Null;

    // Masks
    public const uint FullIdMask   = 0x00FFFFFF;
    public const uint MediumIdMask = 0x0000FFFF;
    public const uint SmallIdMask  = 0x00000FFF;

    // Raw value
    public readonly uint Raw;

    // ID accessors (by master style)
    public uint FullId { get; }                   // Raw & 0x00FFFFFF
    public uint MediumId { get; }                 // Raw & 0x0000FFFF
    public uint LightId { get; }                  // Raw & 0x00000FFF

    // Master index accessors
    public uint FullMasterIndex { get; }          // (Raw & 0xFF000000) >> 24
    public uint MediumMasterIndex { get; }        // (Raw & 0x00FF0000) >> 16
    public uint LightMasterIndex { get; }         // (Raw & 0x00FFF000) >> 12

    // Constructors / factories
    public FormID(uint idWithModIndex);
    public static FormID Factory(ReadOnlySpan<char> hexStr);
    public static bool TryFactory(ReadOnlySpan<char> hexStr, out FormID id, bool strictLength = true);
    public static FormID Factory(ReadOnlySpan<byte> bytes);
    public static FormID Factory(uint idWithModIndex);
    public static FormID Factory(MasterStyle style, uint masterIndex, uint id);

    // Style-aware accessors
    public uint Id(MasterStyle style);
    public string IdString(MasterStyle style);
    public uint MasterIndex(MasterStyle style);
    public static uint IdMask(MasterStyle style);
    public static uint MasterIndexShift(MasterStyle style);

    // Conversion
    public byte[] ToBytes();
}
```

---

### RecordType

A struct representing a four-character record type header used in the binary format to delineate records and subrecords.

**Namespace**: `Mutagen.Bethesda.Plugins`

```csharp
public readonly struct RecordType : IEquatable<RecordType>, IEquatable<string>
{
    public const byte Length = 4;
    public static readonly RecordType Null;       // "\0\0\0\0"

    public readonly int TypeInt;                  // Integer representation
    public string Type { get; }                   // 4-char string
    public string CheckedType { get; }            // With unprintable chars escaped

    // Constructors
    public RecordType(int type);
    public RecordType(ReadOnlySpan<char> typeStr);
    public static bool TryFactory(ReadOnlySpan<char> str, out RecordType recType);

    // Static helpers
    public static string GetStringType(int typeInt);
    public static string GetCheckedStringType(int typeInt);
    public static int GetTypeInt(ReadOnlySpan<char> typeStr);

    // Implicit conversions
    public static implicit operator RecordType(ReadOnlySpan<char> str);
    public static implicit operator RecordType(string str);
}
```

---

### FormLink / IFormLink

A `FormKey` with an associated Major Record Type that provides type safety. `FormLinkGetter<T>` is the readonly base, `FormLink<T>` is the settable version.

**Namespace**: `Mutagen.Bethesda.Plugins`

```csharp
// Readonly interface hierarchy
public interface IFormLinkIdentifier : IFormKeyGetter, ILinkIdentifier { }
public interface IFormLinkGetter : ILink, IFormLinkIdentifier
{
    FormKey? FormKeyNullable { get; }
    bool IsNull { get; }
}
public interface IFormLinkGetter<out TMajorGetter> : ILink<TMajorGetter>, IFormLinkGetter
    where TMajorGetter : class, IMajorRecordGetter
{
    IFormLink<TMajorRet> Cast<TMajorRet>()
        where TMajorRet : class, IMajorRecordGetter;
}

// Settable interface
public interface IFormLink<out TMajorGetter> : IFormLinkGetter<TMajorGetter>, IClearable
    where TMajorGetter : class, IMajorRecordGetter
{
    new FormKey? FormKeyNullable { get; set; }
    new FormKey FormKey { get; set; }
    void SetTo(FormKey? formKey);
    void SetToNull();
}

// Nullable variant
public interface IFormLinkNullableGetter<out TMajorGetter> : IFormLinkGetter<TMajorGetter> { }
public interface IFormLinkNullable<out TMajorGetter> : IFormLink<TMajorGetter> { }

// Concrete classes
public class FormLinkGetter<TMajorGetter> : IFormLinkGetter<TMajorGetter> { ... }
public sealed class FormLink<TMajorGetter> : FormLinkGetter<TMajorGetter>, IFormLink<TMajorGetter>
{
    // Implicit conversions
    public static implicit operator FormLink<TMajorGetter>(TMajorGetter major);
    public static implicit operator FormLink<TMajorGetter>(FormKey formKey);
}
```

---

### FormLinkInformation

A record that pairs a `FormKey` with a `Type`, implementing `IFormLinkGetter`. Useful for storing type-erased FormLink data.

**Namespace**: `Mutagen.Bethesda.Plugins`

```csharp
public sealed record FormLinkInformation(FormKey FormKey, Type Type) : IFormLinkGetter
{
    public static readonly FormLinkInformation Null;

    public static FormLinkInformation Factory<TMajorGetter>(IFormLinkGetter<TMajorGetter> link);
    public static FormLinkInformation Factory(IMajorRecordGetter majorRec);
    public static FormLinkInformation Factory(IFormLinkIdentifier rhs);
    public static bool TryFactory(ReadOnlySpan<char> str, out FormLinkInformation info);
    public static FormLinkInformation Factory(ReadOnlySpan<char> str);
}
```

---

### ILink

Base interface for objects that can resolve against a `LinkCache`.

**Namespace**: `Mutagen.Bethesda.Plugins`

```csharp
public interface ILinkIdentifier
{
    Type Type { get; }
}

public interface ILink : ILinkIdentifier
{
    bool TryGetModKey(out ModKey modKey);
    bool TryResolveFormKey(ILinkCache cache, out FormKey formKey);
    bool TryResolveCommon(ILinkCache cache, out IMajorRecordGetter majorRecord);
}

public interface ILink<out TMajor> : ILink
    where TMajor : IMajorRecordGetter
{
    TMajor? TryResolve(ILinkCache cache);
}
```

---

## Mutagen.Bethesda.Plugins.Records

### IMod / IModGetter

The primary interfaces for Bethesda mod files. `IModGetter` is the read-only interface; `IMod` adds mutation capabilities.

**Namespace**: `Mutagen.Bethesda.Plugins.Records`

```csharp
public interface IModGetter :
    IModFlagsGetter,
    IMajorRecordGetterEnumerable,
    IMajorRecordSimpleContextEnumerable,
    IFormLinkContainerGetter
{
    GameRelease GameRelease { get; }
    IReadOnlyList<IMasterReferenceGetter> MasterReferences { get; }
    IReadOnlyList<IFormLinkGetter<IMajorRecordGetter>>? OverriddenForms { get; }
    uint NextFormID { get; }

    IGroupGetter<TMajor>? TryGetTopLevelGroup<TMajor>() where TMajor : IMajorRecordGetter;
    IGroupGetter? TryGetTopLevelGroup(Type type);

    void WriteToBinary(FilePath path, BinaryWriteParameters? param = null);
    void WriteToBinary(Stream stream, BinaryWriteParameters? param = null);

    uint GetDefaultInitialNextFormID(bool? forceUseLowerFormIDRanges = false);
    uint GetRecordCount();
    IMod DeepCopy();
}

public interface IMod : IModGetter, IMajorRecordEnumerable, IFormKeyAllocator, IFormLinkContainer
{
    new IList<MasterReference> MasterReferences { get; }
    new IGroup<TMajor>? TryGetTopLevelGroup<TMajor>() where TMajor : IMajorRecord;
    new uint NextFormID { get; set; }
    new bool UsingLocalization { get; set; }
    new bool IsSmallMaster { get; set; }
    new bool IsMediumMaster { get; set; }
    new bool IsMaster { get; set; }
    TAlloc SetAllocator<TAlloc>(TAlloc allocator) where TAlloc : IFormKeyAllocator;
}

// For overlay (memory-mapped) mod reading
public interface IModDisposeGetter : IModGetter, IDisposable { }
```

---

### IMajorRecord / IMajorRecordGetter

The core interfaces for individual records within a mod.

**Namespace**: `Mutagen.Bethesda.Plugins.Records`

```csharp
public partial interface IMajorRecordGetter :
    IFormVersionGetter,
    IMajorRecordIdentifierGetter,
    IFormLinkContainerGetter,
    IAssetLinkContainerGetter,
    IFormLinkIdentifier,
    IMajorRecordQueryableGetter
{
    bool IsCompressed { get; }
    bool IsDeleted { get; }
    ushort? FormVersion { get; }
}

public partial interface IMajorRecord : IFormLinkContainer, IAssetLinkContainer, IMajorRecordQueryable
{
    new FormKey FormKey { get; }
    new bool IsCompressed { get; set; }
    new bool IsDeleted { get; set; }
    bool Disable();
}

public partial class MajorRecord : IFormLinkContainer
{
    public virtual String? EditorID { get; set; }
    public string TitleString { get; }            // "EditorID - FormKey"
    public bool IsCompressed { get; set; }
    public bool IsDeleted { get; set; }
    public virtual bool Disable();

    // Comparers
    public static IEqualityComparer<IMajorRecordGetter> FormKeyEqualityComparer { get; }
}

// Extension methods
public static class IMajorRecordGetterExt
{
    public static FormLinkInformation ToFormLinkInformation(this IMajorRecordGetter majorRec);
    public static bool IsInjected(this IMajorRecordGetter majorRec, ILinkCache linkCache);
}
```

---

### IGroup / IGroupGetter

Interfaces for record groups that contain collections of major records.

**Namespace**: `Mutagen.Bethesda.Plugins.Records`

```csharp
public interface IGroupGetter<out TMajor> : IGroupGetter
    where TMajor : IMajorRecordGetter
{
    new IEnumerable<TMajor> Records { get; }
    new IReadOnlyCache<TMajor, FormKey> RecordCache { get; }
    new TMajor this[FormKey key] { get; }
}

public interface IGroupGetter : IGroupCommonGetter
{
    IMod SourceMod { get; }
    IEnumerable<FormKey> FormKeys { get; }
    new IEnumerable<IMajorRecordGetter> Records { get; }
    IReadOnlyCache<IMajorRecordGetter, FormKey> RecordCache { get; }
    IMajorRecordGetter this[FormKey key] { get; }
    bool ContainsKey(FormKey key);
}

public interface IGroup<TMajor> : IGroupGetter<TMajor>, IGroup
    where TMajor : IMajorRecord
{
    new ICache<TMajor, FormKey> RecordCache { get; }
    void Add(TMajor record);
    TMajor AddReturn(TMajor record);
    void Set(TMajor record);
    void Set(IEnumerable<TMajor> records);
    bool Remove(FormKey key);
    void Remove(IEnumerable<FormKey> keys);
}

// List-based group variant (for ordered groups)
public interface IListGroup<TObject> : IListGroupGetter<TObject>, IExtendedList<TObject>
    where TObject : ILoquiObject
{
    new IExtendedList<TObject> Records { get; }
}
```

---

### IModHeaderCommon

Low-level interface for mod header data.

**Namespace**: `Mutagen.Bethesda.Plugins.Records`

```csharp
public interface IModHeaderCommon : IBinaryItem
{
    IExtendedList<MasterReference> MasterReferences { get; }
    int RawFlags { get; set; }
    uint NumRecords { get; set; }
    uint NextFormID { get; set; }
    void SetOverriddenForms(IEnumerable<FormKey>? formKeys);
}
```

---

### ModFactory

Static classes for creating or importing mod objects in a generic or non-generic context.

**Namespace**: `Mutagen.Bethesda.Plugins.Records`

```csharp
// Non-generic factory (uses GameRelease to determine concrete type)
public static class ModFactory
{
    public static IModDisposeGetter ImportGetter(ModPath path, GameRelease release, BinaryReadParameters? param = null);
    public static IMod ImportSetter(ModPath path, GameRelease release, BinaryReadParameters? param = null);
    public static IMod Activator(ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false);
    public static IModDisposeGetter ImportGetterWithMultiFileDetection(
        ModPath modPath, IEnumerable<IModMasterStyledGetter> loadOrder, GameRelease release, BinaryReadParameters? param = null);
    public static IMod ImportSetterWithMultiFileDetection(
        ModPath modPath, IEnumerable<IModMasterStyledGetter> loadOrder, GameRelease release, BinaryReadParameters? param = null);
    public static IModDisposeGetter ImportMultiFileGetter(
        ModKey targetModKey, IEnumerable<ModPath> splitFiles, IEnumerable<IModMasterStyledGetter> loadOrder, GameRelease release, BinaryReadParameters? param = null);
}

// Generic factory (type-safe, resolves to correct game mod type at compile time)
public static class ModFactory<TMod> where TMod : IModGetter
{
    public static readonly ActivatorDelegate Activator;
    public static readonly ImporterDelegate Importer;
    public static readonly ImportMultiFileGetterDelegate ImportMultiFileGetter;
    public static readonly ImportGetterWithMultiFileDetectionDelegate ImportGetterWithMultiFileDetection;
    public static readonly ImportSetterWithMultiFileDetectionDelegate ImportSetterWithMultiFileDetection;
}
```

---

### GenderedItem

A container for male/female paired data, common in Bethesda game records.

**Namespace**: `Mutagen.Bethesda.Plugins.Records`

```csharp
public enum MaleFemaleGender { Male, Female }

public interface IGenderedItemGetter<out T> : IEnumerable<T>
{
    T Male { get; }
    T Female { get; }
    T this[MaleFemaleGender gender] { get; }
}

public interface IGenderedItem<T> : IGenderedItemGetter<T>
{
    new T Male { set; get; }
    new T Female { set; get; }
    new T this[MaleFemaleGender gender] { get; set; }
}

public sealed class GenderedItem<T> : IGenderedItem<T>
{
    public GenderedItem(T male, T female);
}
```

---

### ModFlags / IModFlagsGetter

Interfaces exposing mod header flag information.

**Namespace**: `Mutagen.Bethesda.Plugins.Records`

```csharp
public interface IModMasterStyledGetter : IModKeyed
{
    MasterStyle MasterStyle { get; }
}

public interface IModFlagsGetter : IModMasterStyledGetter
{
    bool CanUseLocalization { get; }
    bool UsingLocalization { get; }
    bool CanBeSmallMaster { get; }
    bool IsSmallMaster { get; }
    bool CanBeMediumMaster { get; }
    bool IsMediumMaster { get; }
    bool IsMaster { get; }
    bool ListsOverriddenForms { get; }
}

public record ModFlags : IModFlagsGetter
{
    public ModFlags(ModKey modKey);
    public ModFlags(IModFlagsGetter flags);
}
```

---

### IMajorRecordIdentifier

Interfaces for identifying records by FormKey and EditorID.

**Namespace**: `Mutagen.Bethesda.Plugins.Records`

```csharp
public interface IFormKeyGetter
{
    FormKey FormKey { get; }
}

public interface IMajorRecordIdentifierGetter : IFormKeyGetter
{
    string? EditorID { get; }
}

public record MajorRecordIdentifier : IMajorRecordIdentifierGetter
{
    public required FormKey FormKey { get; init; }
    public string? EditorID { get; init; }
    public static IEqualityComparer<IMajorRecordIdentifierGetter> EqualityComparer { get; }
}
```

---

## Mutagen.Bethesda.Plugins.Aspects

Cross-record interfaces implemented by Major Records sharing common traits.

### INamed / INamedGetter

```csharp
public interface INamedGetter : INamedRequiredGetter
{
    new String? Name { get; }
}

public interface INamed : INamedGetter, INamedRequired
{
    new String? Name { get; set; }
}
```

**Extension methods** (`INamedExt`):
```csharp
public static bool NamedFieldsContain<TMajor>(this TMajor named, string str)
    where TMajor : INamedGetter, IMajorRecordGetter;
public static bool NamedFieldsContain<TMajor>(this TMajor named, string str, StringComparison comparison)
    where TMajor : INamedGetter, IMajorRecordGetter;
```

### ITranslatedNamed / ITranslatedNamedGetter

For records with translatable names (supporting multiple languages).

```csharp
public interface ITranslatedNamedGetter : ITranslatedNamedRequiredGetter, INamedGetter
{
    new ITranslatedStringGetter? Name { get; }
}

public interface ITranslatedNamed : ITranslatedNamedRequired, ITranslatedNamedGetter, INamed
{
    new TranslatedString? Name { get; set; }
}
```

### IKeyworded / IKeywordedGetter

For records that have keyword lists.

```csharp
public interface IKeywordedGetter<TKeyword> : IKeywordedGetter
    where TKeyword : class, IKeywordCommonGetter
{
    new IReadOnlyList<IFormLinkGetter<TKeyword>>? Keywords { get; }
}

public interface IKeyworded<TKeyword> : IKeywordedGetter<TKeyword>
    where TKeyword : class, IKeywordCommonGetter
{
    new ExtendedList<IFormLinkGetter<TKeyword>>? Keywords { get; set; }
}
```

**Extension methods** (`IKeywordedExt`):
```csharp
public static bool HasKeyword<TKeyword>(this IKeywordedGetter<TKeyword> keyworded, FormKey keywordKey);
public static bool HasKeyword<TKeyword>(this IKeywordedGetter<TKeyword> keyworded, IFormLinkGetter<TKeyword> keywordLink);
public static bool HasKeyword<TKeyword>(this IKeywordedGetter<TKeyword> keyworded, TKeyword keyword);
public static bool HasKeyword<TKeyword>(this IKeywordedGetter<TKeyword> keyworded, string editorID, ILinkCache cache, StringComparison comparison = OrdinalIgnoreCase);
public static bool TryResolveKeyword<TKeyword>(this IKeywordedGetter<TKeyword> keyworded, FormKey keywordKey, ILinkCache cache, out TKeyword keyword);
public static bool TryResolveKeyword<TKeyword>(this IKeywordedGetter<TKeyword> keyworded, string editorID, ILinkCache cache, out TKeyword keyword, StringComparison comparison = OrdinalIgnoreCase);
public static bool HasAnyKeyword<TKeyword>(this IKeywordedGetter<TKeyword> keyworded, IEnumerable<FormKey> keywordKeys);
public static bool HasAnyKeyword<TKeyword>(this IKeywordedGetter<TKeyword> keyworded, IEnumerable<string> editorIDs, ILinkCache cache, StringComparison comparison = OrdinalIgnoreCase);
```

### IWeightValue / IWeightValueGetter

For records with weight and gold value fields.

```csharp
public interface IWeightValueGetter : IMajorRecordQueryableGetter
{
    uint Value { get; }
    float Weight { get; }
}

public interface IWeightValue : IWeightValueGetter
{
    new uint Value { get; set; }
    new float Weight { get; set; }
}
```

---

## Mutagen.Bethesda.Plugins.Cache

### ILinkCache

The primary interface for resolving record references. Provides lookups by FormKey, EditorID, or FormLink.

**Namespace**: `Mutagen.Bethesda.Plugins.Cache`

```csharp
public interface ILinkCache : IIdentifierLinkCache, IWinningOverrideProvider
{
    // By FormKey (generic, preferred -- faster and type-safe)
    bool TryResolve<TMajor>(FormKey formKey, out TMajor majorRec, ResolveTarget target = Winner)
        where TMajor : class, IMajorRecordQueryableGetter;
    bool TryResolve<TMajor>(IFormLinkGetter<TMajor> formLink, out TMajor majorRec, ResolveTarget target = Winner)
        where TMajor : class, IMajorRecordGetter;

    // By EditorID (generic)
    bool TryResolve<TMajor>(string editorId, out TMajor majorRec)
        where TMajor : class, IMajorRecordQueryableGetter;

    // By FormKey (non-generic, slower -- scans all record types)
    [Obsolete] bool TryResolve(FormKey formKey, out IMajorRecordGetter majorRec, ResolveTarget target = Winner);
    [Obsolete] bool TryResolve(string editorId, out IMajorRecordGetter majorRec);

    // By Type
    bool TryResolve(FormKey formKey, Type type, out IMajorRecordGetter majorRec, ResolveTarget target = Winner);
    bool TryResolve(string editorId, Type type, out IMajorRecordGetter majorRec);

    // Multi-type resolution
    bool TryResolve(FormKey formKey, IEnumerable<Type> types, out IMajorRecordGetter majorRec, ResolveTarget target = Winner);
    bool TryResolve(FormKey formKey, IEnumerable<Type> types, out IMajorRecordGetter majorRec, out Type matchedType, ResolveTarget target = Winner);

    // Resolve (throws on failure)
    [Obsolete] IMajorRecordGetter Resolve(FormKey formKey, ResolveTarget target = Winner);
    TMajor Resolve<TMajor>(FormKey formKey, ResolveTarget target = Winner);
    // ... additional Resolve overloads mirror TryResolve
}
```

---

### IModContext

A pairing of a record with knowledge of where it came from, enabling insertion into new mods.

**Namespace**: `Mutagen.Bethesda.Plugins.Cache`

```csharp
public interface IModContext
{
    ModKey ModKey { get; }
    IModContext? Parent { get; }
    object? Record { get; }
}

public interface IModContext<out T> : IModContext
{
    new T Record { get; }
    bool TryGetParentSimpleContext<TTargetGetter>(out IModContext<TTargetGetter> parent);
}

public interface IModContext<TMod, TModGetter, out TTarget, out TTargetGetter> : IModContext<TTargetGetter>
    where TModGetter : IModGetter
    where TMod : TModGetter, IMod
{
    TTarget GetOrAddAsOverride(TMod mod);
    TTarget DuplicateIntoAsNewRecord(TMod mod, FormKey? formKey = null);
    TTarget DuplicateIntoAsNewRecord(TMod mod, string? editorID);
    bool TryGetParentContext<TScopedTarget, TScopedTargetGetter>(
        out IModContext<TMod, TModGetter, TScopedTarget, TScopedTargetGetter> parent);
}
```

---

### ResolveTarget

```csharp
public enum ResolveTarget
{
    Winner,   // Locate the winning override
    Origin    // Locate the original definition
}
```

### LinkCachePreferences

Configuration for how a link cache operates (defined in `LinkCachePreferences.cs`). Controls aspects like retention policy.

---

## Mutagen.Bethesda.Plugins.Order

### LoadOrder

Static utility class for working with load orders.

**Namespace**: `Mutagen.Bethesda.Plugins.Order`

```csharp
public static partial class LoadOrder
{
    // Timestamp alignment
    public static bool NeedsTimestampAlignment(GameCategory game);
    public static IEnumerable<ILoadOrderListingGetter> AlignToTimestamps(
        IEnumerable<ILoadOrderListingGetter> incomingLoadOrder,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true);
    public static void AlignTimestamps(
        IEnumerable<ModKey> loadOrder,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true,
        DateTime? startDate = null,
        TimeSpan? interval = null);

    // Load order retrieval
    public static IEnumerable<ILoadOrderListingGetter> GetListings(
        GameRelease game,
        DirectoryPath dataPath,
        bool throwOnMissingMods = true,
        IFileSystem? fileSystem = null);
}
```

---

### LoadOrderListing / ILoadOrderListingGetter

Represents a single entry in a load order with enabled/disabled and ghosting state.

```csharp
public interface ILoadOrderListingGetter : IModKeyed
{
    bool Enabled { get; }
    bool Ghosted { get; }
    string GhostSuffix { get; }
    string FileName { get; }
}

public sealed record LoadOrderListing : ILoadOrderListingGetter
{
    public ModKey ModKey { get; init; }
    public bool Enabled { get; init; }
    public string GhostSuffix { get; init; }

    public LoadOrderListing(ModKey modKey, bool enabled, string ghostSuffix = "");

    // Factory methods
    public static LoadOrderListing CreateEnabled(ModKey modKey);
    public static LoadOrderListing CreateDisabled(ModKey modKey);
    public static LoadOrderListing CreateGhosted(ModKey modKey, string ghostSuffix);
    public static bool TryFromString(ReadOnlySpan<char> str, bool enabledMarkerProcessing, out LoadOrderListing listing);
    public static LoadOrderListing FromString(ReadOnlySpan<char> str, bool enabledMarkerProcessing);

    // Implicit conversion
    public static implicit operator LoadOrderListing(ModKey modKey);
}
```

---

### ModListing / IModListingGetter

A load order listing that also tracks whether the mod file exists on disk. `ModListing<TMod>` adds an optional reference to the loaded mod object.

```csharp
public interface IModListingGetter : ILoadOrderListingGetter
{
    bool ModExists { get; }
}

public sealed record ModListing : IModListingGetter { ... }

public sealed record ModListing<TMod> : IModListing<TMod>
    where TMod : class, IModKeyed
{
    public TMod? Mod { get; init; }
    public bool ModExists => Mod != null;
}
```

---

## Mutagen.Bethesda.Plugins.Allocators

### IFormKeyAllocator

Interface for allocating new FormKeys from a mod.

**Namespace**: `Mutagen.Bethesda.Plugins.Allocators`

```csharp
public interface IFormKeyAllocator
{
    FormKey GetNextFormKey();
    FormKey GetNextFormKey(string? editorID);
}

public abstract class BaseFormKeyAllocator : IFormKeyAllocator
{
    public IMod Mod { get; }
    protected BaseFormKeyAllocator(IMod mod);
    public abstract FormKey GetNextFormKey();
    protected abstract FormKey GetNextFormKeyNotNull(string editorID);
}
```

Concrete implementations: `SimpleFormKeyAllocator`, `TextFileFormKeyAllocator`, `TextFileSharedFormKeyAllocator`.

---

## Mutagen.Bethesda.Plugins.Meta

### GameConstants

Readonly singletons containing alignment and length constants for each game's binary format.

**Namespace**: `Mutagen.Bethesda.Plugins.Meta`

```csharp
public sealed record GameConstants
{
    public GameRelease Release { get; init; }
    public sbyte ModHeaderLength { get; }
    public sbyte ModHeaderFluffLength { get; }
    public GroupConstants GroupConstants { get; }
    public MajorRecordConstants MajorConstants { get; }
    public RecordHeaderConstants SubConstants { get; }
    public ReadOnlyMemorySlice<Language> Languages { get; }
    public EncodingBundle Encodings { get; }
    public bool HasEnabledMarkers { get; init; }
    public ushort? DefaultFormVersion { get; init; }
    public float? DefaultModHeaderVersion { get; init; }
    public string? MyDocumentsString { get; init; }
    public bool PluginsFileInGameFolder { get; init; }
    public string IniName { get; init; }
    public uint DefaultHighRangeFormID { get; init; }
    public bool UsesStrings { get; }
    public bool SeparateMasterLoadOrders { get; init; }
    public string DataFolderRelativePath { get; init; }
    public int? SmallMasterFlag { get; }
    public int? MediumMasterFlag { get; }

    // Singletons for each game
    public static readonly GameConstants Oblivion;
    public static readonly GameConstants OblivionRE;
    public static readonly GameConstants SkyrimLE;
    public static readonly GameConstants SkyrimSE;
    public static readonly GameConstants SkyrimVR;
    public static readonly GameConstants Fallout4;
    public static readonly GameConstants Fallout4VR;
    public static readonly GameConstants Starfield;

    public static GameConstants Get(GameRelease release);
}
```

---

## Mutagen.Bethesda.Plugins.Masters

### MasterStyle

Enum representing the different master file flag styles that control FormID format interpretation.

**Namespace**: `Mutagen.Bethesda.Plugins.Masters`

```csharp
public enum MasterStyle
{
    Full,     // Standard master, FormID uses 0xFF000000 for master index, 0x00FFFFFF for ID
    Small,    // Light/ESL master (0xFE prefix), 12-bit ID, 12-bit master index
    Medium    // Medium master (0xFD prefix), 16-bit ID, 8-bit master index
}
```

---

## Mutagen.Bethesda.Plugins.Binary

### Headers

Readonly struct overlays for reading binary record headers without allocation.

**Key types** in `Mutagen.Bethesda.Plugins.Binary.Headers`:

- `ModHeader` -- Mod file header
- `GroupHeader` -- Group record header (GRUP)
- `MajorRecordHeader` -- Major record header (e.g., NPC_, WEAP)
- `SubrecordHeader` -- Subrecord header (e.g., EDID, FULL)

### Binary Parameters

Configuration types for reading and writing mod files.

**Key types** in `Mutagen.Bethesda.Plugins.Binary.Parameters`:

```csharp
public sealed class BinaryReadParameters
{
    public IFileSystem? FileSystem { get; init; }
    public StringsReadParameters? StringsParam { get; init; }
    // ... additional read configuration
}

public sealed class BinaryWriteParameters
{
    public ModKeyOption ModKey { get; init; }
    public RecordCountOption RecordCount { get; init; }
    public NextFormIDOption NextFormID { get; init; }
    public FormIDUniquenessOption FormIDUniqueness { get; init; }
    public MastersListOrderingOption MastersListOrdering { get; init; }
    public MastersListContentOption MastersListContent { get; init; }
    public FormIDCompactionOption FormIDCompaction { get; init; }
    public OverriddenFormsOption OverriddenForms { get; init; }
    public ALowerRangeDisallowedHandlerOption LowerRangeDisallowedHandler { get; init; }
    public ParallelWriteParameters Parallel { get; init; }
    public IFileSystem? FileSystem { get; init; }
    // ... additional write configuration
}
```

### Streams

Binary reading/writing stream types.

**Key types** in `Mutagen.Bethesda.Plugins.Binary.Streams`:

- `IMutagenReadStream` -- Core reading stream interface
- `MutagenBinaryReadStream` -- Standard binary read stream
- `MutagenFrame` -- Frame-limited read stream (reads within a length boundary)
- `MutagenWriter` -- Binary writer with record header helpers
- `MutagenMemoryReadStream` -- Memory-backed read stream

---

## Mutagen.Bethesda.Plugins.Assets

### IAssetLink / AssetLink

Links to game asset files (meshes, textures, sounds, etc.) with type safety.

**Namespace**: `Mutagen.Bethesda.Plugins.Assets`

```csharp
public interface IAssetLinkGetter
{
    IAssetType AssetTypeInstance { get; }
    string GivenPath { get; }
    DataRelativePath DataRelativePath { get; }
    string Extension { get; }
    IAssetType Type { get; }
    bool IsNull { get; }
}

public interface IAssetLink : IAssetLinkGetter
{
    bool TrySetPath(DataRelativePath? path);
    bool TrySetPath(string? path);
    new string GivenPath { get; set; }
}

// Generic typed variants
public interface IAssetLinkGetter<out TAssetType> : IAssetLinkGetter
    where TAssetType : IAssetType { }
public interface IAssetLink<out TAssetType> : IAssetLink
    where TAssetType : IAssetType { }

// Concrete class
public class AssetLinkGetter<TAssetType> : IAssetLinkGetter<TAssetType>
    where TAssetType : class, IAssetType { ... }
```

---

## Mutagen.Bethesda.Archives

### Archive

Static class for working with Bethesda archive files (BSA/BA2).

**Namespace**: `Mutagen.Bethesda.Archives`

```csharp
public static class Archive
{
    public static string GetExtension(GameRelease release);  // ".bsa" or ".ba2"

    public static IArchiveReader CreateReader(
        GameRelease release, FilePath path, IFileSystem? fileSystem = null);

    public static IEnumerable<FilePath> GetApplicableArchivePaths(
        GameRelease release, DirectoryPath dataFolderPath,
        IFileSystem? fileSystem = null, bool returnEmptyIfMissing = true);

    public static IEnumerable<FilePath> GetApplicableArchivePaths(
        GameRelease release, DirectoryPath dataFolderPath,
        ModKey modKey, IFileSystem? fileSystem = null, bool returnEmptyIfMissing = true);

    public static bool IsApplicable(GameRelease release, ModKey modKey, FileName archiveFileName);

    public static IEnumerable<FileName> GetIniListings(GameRelease release, IFileSystem? fileSystem = null);
    public static IEnumerable<FileName> GetIniListings(GameRelease release, FilePath path, IFileSystem? fileSystem = null);
    public static IEnumerable<FileName> GetIniListings(GameRelease release, Stream iniStream, IFileSystem? fileSystem = null);
}
```

### IArchiveReader

```csharp
public interface IArchiveReader
{
    bool TryGetFolder(string path, out IArchiveFolder folder);
    IEnumerable<IArchiveFile> Files { get; }
}
```

### IArchiveFile

```csharp
public interface IArchiveFile
{
    string Path { get; }
    uint Size { get; }
    byte[] GetBytes();
    ReadOnlySpan<byte> GetSpan();
    ReadOnlyMemorySlice<byte> GetMemorySlice();
    Stream AsStream();
}
```

### IArchiveFolder

```csharp
public interface IArchiveFolder
{
    string? Path { get; }
    IReadOnlyCollection<IArchiveFile> Files { get; }
}
```

---

## Mutagen.Bethesda.Environments

### GameEnvironment

Singleton entry point for constructing a game environment automatically from detected game installations.

**Namespace**: `Mutagen.Bethesda.Environments`

```csharp
public sealed class GameEnvironment
{
    public static readonly GameEnvironment Typical;

    public IGameEnvironment<TModSetter, TModGetter> Construct<TModSetter, TModGetter>(
        GameRelease release, LinkCachePreferences? linkCachePrefs = null)
        where TModSetter : class, IContextMod<TModSetter, TModGetter>, TModGetter
        where TModGetter : class, IContextGetterMod<TModSetter, TModGetter>;

    public IGameEnvironment<TModGetter> Construct<TModGetter>(
        GameRelease release, LinkCachePreferences? linkCachePrefs = null)
        where TModGetter : class, IModGetter;

    public IGameEnvironment Construct(
        GameRelease release, LinkCachePreferences? linkCachePrefs = null);
}
```

### IGameEnvironment

```csharp
public interface IGameEnvironment : IDisposable
{
    DirectoryPath DataFolderPath { get; }
    GameRelease GameRelease { get; }
    FilePath? LoadOrderFilePath { get; }
    FilePath? CreationClubListingsFilePath { get; }
    ILoadOrderGetter<IModListingGetter<IModGetter>> LoadOrder { get; }
    ILinkCache LinkCache { get; }
    IAssetProvider AssetProvider { get; }
}

public interface IGameEnvironment<TMod> : IGameEnvironment
    where TMod : class, IModGetter
{
    new ILoadOrderGetter<IModListingGetter<TMod>> LoadOrder { get; }
}

public interface IGameEnvironment<TModSetter, TModGetter> : IGameEnvironment<TModGetter>
    where TModSetter : class, IContextMod<TModSetter, TModGetter>, TModGetter
    where TModGetter : class, IContextGetterMod<TModSetter, TModGetter>
{
    new ILinkCache<TModSetter, TModGetter> LinkCache { get; }
}
```

### GameEnvironmentState

Concrete implementation of `IGameEnvironment` with static `Construct` factory methods.

```csharp
public sealed class GameEnvironmentState : IGameEnvironment
{
    public static IGameEnvironment Construct(
        GameRelease release, DirectoryPath gameFolder, LinkCachePreferences? linkCachePrefs = null);
}

public sealed class GameEnvironmentState<TModSetter, TModGetter> : IGameEnvironment<TModSetter, TModGetter>
{
    public static IGameEnvironment<TModSetter, TModGetter> Construct(
        GameRelease release, DirectoryPath gameFolder, LinkCachePreferences? linkCachePrefs = null);
}
```

### GameEnvironmentBuilder

Builder pattern for constructing game environments with custom configuration.

```csharp
public sealed record GameEnvironmentBuilder<TMod, TModGetter>
{
    public static GameEnvironmentBuilder<TMod, TModGetter> Create(GameRelease release);

    public GameEnvironmentBuilder<TMod, TModGetter> TransformLoadOrderListings(
        Func<IEnumerable<ILoadOrderListingGetter>, IEnumerable<ILoadOrderListingGetter>> transformer);
    public GameEnvironmentBuilder<TMod, TModGetter> WithLoadOrder(params ModKey[] modKeys);
    public GameEnvironmentBuilder<TMod, TModGetter> WithLoadOrder(params ILoadOrderListingGetter[] listings);
    // ... additional builder methods
    public IGameEnvironment<TMod, TModGetter> Build();
}
```

---

## Mutagen.Bethesda.Installs

### GameLocations

Static utility for locating game installations on the local system (checks Steam, GOG, Registry, Xbox).

**Namespace**: `Mutagen.Bethesda.Installs`

```csharp
public static class GameLocations
{
    public static IEnumerable<DirectoryPath> GetGameFolders(GameRelease release);
    public static bool TryGetGameFolder(GameRelease release, out DirectoryPath path);
    public static DirectoryPath GetGameFolder(GameRelease release);
    public static bool TryGetDataFolder(GameRelease release, out DirectoryPath path);
    public static DirectoryPath GetDataFolder(GameRelease release);
}
```

**Game source providers** (`Mutagen.Bethesda.Installs`):
- `SteamGameSource` -- Locates via Steam
- `GogGameSource` -- Locates via GOG Galaxy
- `RegistryGameSource` -- Locates via Windows Registry
- `XboxGameSource` -- Locates via Xbox/Microsoft Store

---

## Mutagen.Bethesda.Strings

### Language

Enum of all supported languages for localized strings.

**Namespace**: `Mutagen.Bethesda.Strings`

```csharp
public enum Language
{
    English,
    German,
    Italian,
    Spanish,
    Spanish_Mexico,
    French,
    Polish,
    Portuguese_Brazil,
    Chinese,
    Russian,
    Japanese,
    Czech,
    Hungarian,
    Danish,
    Finnish,
    Greek,
    Norwegian,
    Swedish,
    Turkish,
    Arabic,
    Korean,
    Thai,
    ChineseSimplified
}
```

### ITranslatedString / TranslatedString

A thread-safe string that can be represented in multiple languages.

**Namespace**: `Mutagen.Bethesda.Strings`

```csharp
public interface ITranslatedStringGetter : IEnumerable<KeyValuePair<Language, string>>
{
    Language TargetLanguage { get; }
    string? String { get; set; }
    bool TryLookup(Language language, out string str);
    TranslatedString DeepCopy();
    int NumLanguages { get; }
}

public interface ITranslatedString : ITranslatedStringGetter
{
    void Set(Language language, string str);
    void RemoveNonDefault(Language language);
    void ClearNonDefault();
    void Clear();
    new string? String { get; set; }
}

public sealed class TranslatedString : ITranslatedString, IEquatable<TranslatedString>
{
    // Static configuration
    public static bool DefaultLanguageComparisonOnly;
    public static Language DefaultLanguage { get; set; }

    // Thread-safe -- uses internal lock
}
```

**Extension methods** (`TranslatedStringExt`):
```csharp
public static string? Lookup(this ITranslatedStringGetter getter, Language language);
```

**Additional string types**:
- `StringsSource` -- Enum for strings file source (Normal, IL, DL)
- `StringsFileFormat` -- Format of strings files
- `MutagenEncoding` -- Encoding helpers for Bethesda file formats
- `StringsWriter` -- Writes localized string tables to disk

---

## Mutagen.Bethesda.Assets

### IAssetType

Interface for defining game asset file types.

**Namespace**: `Mutagen.Bethesda.Assets`

```csharp
public interface IAssetType
{
    static virtual IAssetType Instance => null!;
    string BaseFolder { get; }
    IEnumerable<string> FileExtensions { get; }
}
```

### DataRelativePath

A struct representing a file path relative to the game's `Data` directory. Automatically strips absolute paths and `Data\` prefixes.

**Namespace**: `Mutagen.Bethesda.Assets`

```csharp
public readonly struct DataRelativePath : IEquatable<DataRelativePath>, IComparable<DataRelativePath>
{
    public static readonly string NullPath;
    public static readonly StringComparison PathComparison;  // OrdinalIgnoreCase

    public string Path { get; }
    public string Extension { get; }
    public bool IsNull { get; }

    public DataRelativePath(string rawPath);

    // Implicit conversions
    public static implicit operator DataRelativePath(string path);
    public static implicit operator DataRelativePath(FilePath path);
    public static implicit operator DataRelativePath(FileInfo info);
}
```

---

## Mutagen.Bethesda.Pex

Papyrus script (PEX) binary file parsing.

### PexFile

Represents a compiled Papyrus script file. Created via `PexFile.CreateFromBinary()`.

**Namespace**: `Mutagen.Bethesda.Pex`

```csharp
public partial class PexFile
{
    public PexFile(GameCategory gameCategory);

    // Header fields (generated)
    public byte MajorVersion { get; set; }
    public byte MinorVersion { get; set; }
    public ushort GameId { get; set; }
    public DateTime CompilationTime { get; set; }
    public string SourceFileName { get; set; }
    public string Username { get; set; }
    public string MachineName { get; set; }
    public DebugInfo? DebugInfo { get; set; }
    public Dictionary<byte, string> UserFlags { get; }
    public ExtendedList<PexObject> Objects { get; }

    // I/O
    public static PexFile CreateFromBinary(FilePath path, GameCategory gameCategory);
    public void WriteToBinary(FilePath path);
}
```

**Key PEX sub-types** (all in `Mutagen.Bethesda.Pex`):
- `PexObject` -- A script class/object
- `PexObjectFunction` -- A function within an object
- `PexObjectVariable` -- A variable declaration
- `PexObjectProperty` -- A property declaration
- `PexObjectState` -- A script state
- `DebugInfo` -- Debug information block

### Pex Enums

```csharp
public enum DebugFunctionType { ... }
public enum FunctionFlags { ... }
public enum InstructionOpcode { ... }    // All Papyrus VM opcodes
public enum PropertyFlags { ... }
public enum VariableType { ... }
```

---

## Plugins.Exceptions

Notable exception types in `Mutagen.Bethesda.Plugins.Exceptions`:

| Exception | Description |
|-----------|-------------|
| `RecordException` | Wraps exceptions with record context (FormKey, EditorID) |
| `SubrecordException` | Wraps exceptions with subrecord context |
| `MissingModException` | A referenced mod file could not be found |
| `MissingRecordException` | A FormKey could not be resolved |
| `LinkCacheMissingException` | Link cache resolution failed |
| `TooManyMastersException` | Mod exceeds the master reference limit |
| `MalformedDataException` | Binary data does not match expected format |
| `RecordCollisionException` | Duplicate FormKey in a group |
| `ModHeaderMalformedException` | Mod header data is invalid |
| `SplitModException` | Error in split mod file handling |
| `UnmappableFormIDException` | FormID cannot be mapped to a master |
| `FormIDCompactionOutOfBoundsException` | FormID compaction exceeded allowed range |
| `LowerFormIDRangeDisallowedException` | Record uses disallowed lower FormID range |

---

## Plugins.IO

File I/O utilities in `Mutagen.Bethesda.Plugins.IO`:

```csharp
public enum AssociatedModFileCategory { ... }  // Categories of files associated with a mod
```

**DI services**:
- `AssociatedFilesLocator` -- Finds files associated with a mod (strings, translations, etc.)
- `ModFilesMover` -- Moves mod files and their associated files

---

## Plugins.Implicit

Manages implicit (hardcoded) game data like base master files and built-in FormKeys.

**Key types** in `Mutagen.Bethesda.Plugins.Implicit`:
- `ImplicitBaseMasters` -- Base master files per game (e.g., Skyrim.esm)
- `ImplicitListings` -- Implicit load order entries
- `ImplicitRecordFormKeys` -- FormKeys of records implicitly defined by the engine
- `Implicits` -- Static access point for all implicit data

---

## Plugins.Converters

JSON/TypeConverter implementations for serializing Mutagen types:

- `FormKeyTypeConverter` -- JSON converter for `FormKey`
- `ModKeyTypeConverter` -- JSON converter for `ModKey`
- `ModPathTypeConverter` -- JSON converter for `ModPath`

---

## WPF.Reflection.Attributes

Attributes for WPF UI integration and serialization hints (used by Mutagen.Bethesda.WPF for settings UI generation):

| Attribute | Description |
|-----------|-------------|
| `FormLinkPickerCustomization` | Customizes FormLink picker behavior |
| `Ignore` | Excludes a property from UI generation |
| `JsonDiskName` | Specifies disk name for JSON serialization |
| `MaintainOrder` | Preserves collection ordering in UI |
| `ObjectNameMember` | Specifies which member to use as display name |
| `SettingName` | Overrides display name in settings UI |
| `StaticEnumDictionary` | Marks a dictionary as using static enum keys |
| `Tooltip` | Adds tooltip text to a setting |
