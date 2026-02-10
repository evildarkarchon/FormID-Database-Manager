# Mutagen.Bethesda.WPF

WPF controls, value converters, and reflection-based settings UI for Mutagen/Bethesda modding applications. Provides ready-to-use FormKey/ModKey picker controls, load order views, and an auto-generated settings editor driven by reflection.

**Target Frameworks:** net8.0, net9.0, net10.0
**Platform:** Windows (WPF)
**Key Dependencies:** Noggog.WPF, ReactiveUI.Fody, Extended.Wpf.Toolkit, Humanizer.Core, ReactiveMarbles.ObservableEvents.SourceGenerator, Mutagen.Bethesda.Core, Mutagen.Bethesda.Kernel

---

## Mutagen.Bethesda.WPF.Plugins

### Enum: FormKeyPickerSearchMode

Defines the active search mode for FormKey picker controls.

```csharp
public enum FormKeyPickerSearchMode
{
    None,
    EditorID,
    FormKey
}
```

---

### Class: AFormKeyPicker

Abstract base class for FormKey picker WPF controls. Extends `NoggogControl`. Provides reactive lookup of FormKeys against an `ILinkCache`, with support for searching by EditorID or FormKey string, scoped type filtering, and status indication.

**Template Parts:**
- `PART_EditorIDBox` (TextBox)
- `PART_FormKeyBox` (TextBox)

**Dependency Properties:**

| Property | Type | Binding | Description |
|---|---|---|---|
| `LinkCache` | `ILinkCache?` | OneWay | The link cache used for record resolution |
| `ScopedTypes` | `IEnumerable?` | OneWay | Collection of `Type` to scope resolution to specific record types |
| `Found` | `bool` | TwoWay | Whether the current FormKey was successfully resolved |
| `Processing` | `bool` | OneWay | Whether a lookup operation is in progress |
| `FormKey` | `FormKey` | TwoWay | The currently selected FormKey value |
| `SelectedType` | `Type?` | OneWay | The matched record type after resolution |
| `FormKeyStr` | `string` | TwoWay | Raw string representation of the FormKey |
| `EditorID` | `string` | TwoWay | The EditorID string for lookup |
| `MissingMeansError` | `bool` | OneWay | If true (default), unresolvable FormKeys are treated as errors |
| `MissingMeansNull` | `bool` | OneWay | If true, unresolvable FormKeys produce `FormKey.Null` |
| `Status` | `StatusIndicatorState` | ReadOnly | Current resolution status (Success/Failure/Passive) |
| `StatusString` | `string` | ReadOnly | Human-readable status message |
| `PickerClickCommand` | `ICommand` | TwoWay | Command invoked when a search result is selected |
| `InSearchMode` | `bool` | ReadOnly | Whether the control is in active search mode |
| `SearchMode` | `FormKeyPickerSearchMode` | ReadOnly | The current search mode (None/EditorID/FormKey) |
| `AllowsSearchMode` | `bool` | TwoWay | Whether search mode is permitted (default: true) |
| `ApplicableEditorIDs` | `IEnumerable` | ReadOnly | Filtered collection of matching record identifiers |
| `ViewingAllowedTypes` | `bool` | TwoWay | Whether the allowed types panel is visible |
| `ViewAllowedTypesCommand` | `ICommand` | OneWay | Toggles the `ViewingAllowedTypes` flag |

**Theming Properties:**

| Property | Type | Description |
|---|---|---|
| `ProcessingSpinnerForeground` | `Brush` | Foreground brush for the processing spinner |
| `ProcessingSpinnerGlow` | `Color` | Glow color for the processing spinner |
| `ErrorBrush` | `Brush` | Brush for error status indication |
| `SuccessBrush` | `Brush` | Brush for success status indication |
| `PassiveBrush` | `Brush` | Brush for passive/neutral status indication |

**Key Methods:**

```csharp
// Returns scoped types or defaults to IMajorRecordGetter
protected IEnumerable<Type> ScopedTypesInternal(IEnumerable? types)

// Wires up template child text boxes for search mode toggling
public override void OnApplyTemplate()
```

**Behavior:** On load, establishes three reactive pipelines:
1. **FormKey -> Lookup:** Observes `FormKey` changes, resolves via `ILinkCache.TryResolveIdentifier`, updates `EditorID`, `Status`, `Found`.
2. **EditorID -> Lookup:** Observes `EditorID` changes, resolves to a `FormKey`.
3. **FormKeyStr -> Lookup:** Parses raw string input, resolves to `FormKey` and `EditorID`.

All pipelines throttle at 100ms and execute resolution on the task pool scheduler with results marshalled back to the UI thread.

---

### Class: FormKeyPicker

Concrete single-selection FormKey picker. Extends `AFormKeyPicker`.

**Additional Dependency Properties:**

| Property | Type | Default | Description |
|---|---|---|---|
| `MaxSearchBoxHeight` | `double` | 1000 | Maximum height of the search results dropdown |
| `SearchBoxHeight` | `double` | NaN | Explicit height of the search results dropdown |

**Behavior:** When a search result is clicked (via `PickerClickCommand`), sets `FormKey` to the selected `IMajorRecordIdentifierGetter.FormKey` and exits search mode.

---

### Class: FormKeyMultiPicker

Multi-selection FormKey picker with an editable list of FormKeys. Extends `AFormKeyPicker`.

**Template Parts:**
- `PART_AddedFormKeyListBox` (ListBox) -- displays selected FormKeys with drag-and-drop reordering

**Additional Dependency Properties:**

| Property | Type | Description |
|---|---|---|
| `FormKeys` | `IList<FormKey>?` | Backing list of selected FormKey values |
| `SelectedFormKey` | `FormKey?` | Currently selected FormKey in the list (TwoWay) |
| `SelectedFormKeyViewModel` | `SelectedVm<FormKey>?` | Selection wrapper VM for the list (TwoWay) |
| `AddFormKeyCommand` | `ICommand` | Adds the current `FormKey` to `FormKeys` (enabled when FormKey is not null) |
| `DeleteSelectedItemsCommand` | `ICommand` | Removes all selected items from `FormKeys` |
| `SelectedForegroundBrush` | `Brush` | Foreground brush for selected items |
| `ItemHoverBrush` | `Brush` | Brush for hovered items |
| `SelectedBackgroundBrush` | `Brush` | Background brush for selected items |

**Public Properties:**

```csharp
public IDerivativeSelectedCollection<FormKey> FormKeySelectionViewModels { get; }
```

---

### Class: FormKeyBox

Simple text-entry control for FormKey values. Extends `NoggogControl`. Provides two-way sync between a `FormKey` value and a raw string representation with validation.

**Dependency Properties:**

| Property | Type | Description |
|---|---|---|
| `FormKey` | `FormKey` | The FormKey value (TwoWay) |
| `RawString` | `string` | Raw text representation (TwoWay) |
| `Error` | `ErrorResponse` | ReadOnly validation error state |
| `Watermark` | `string` | Placeholder text (default: "FormKey") |

**Behavior:** Bidirectional sync between `FormKey` and `RawString`:
- When `FormKey` changes, updates `RawString` to the string representation.
- When `RawString` changes, attempts `FormKey.TryFactory()`. On success, updates `FormKey` and clears error. On failure, sets `FormKey.Null` and reports error.

---

### Class: AModKeyPicker

Abstract base class for ModKey picker WPF controls. Extends `NoggogControl`.

**Template Parts:**
- `PART_ModKeyBox` (ModKeyBox)

**Dependency Properties:**

| Property | Type | Binding | Description |
|---|---|---|---|
| `ModKey` | `ModKey` | TwoWay | The currently selected ModKey |
| `FileName` | `string` | TwoWay | File name string for search filtering |
| `InSearchMode` | `bool` | ReadOnly | Whether the control is in active search mode |
| `Processing` | `bool` | OneWay | Whether a lookup is in progress |
| `AllowsSearchMode` | `bool` | TwoWay | Whether search mode is permitted (default: true) |
| `SearchableMods` | `object` | OneWay | Source of searchable mods (accepts various types -- see below) |
| `ApplicableMods` | `IEnumerable<ModKey>` | ReadOnly | Filtered collection of matching ModKeys |
| `PickerClickCommand` | `ICommand` | TwoWay | Command invoked when a search result is selected |

**SearchableMods accepted types:**
- `IObservable<IChangeSet<IModListingGetter>>` -- live observable load order
- `IObservable<IChangeSet<ModKey>>` -- live observable ModKey collection
- `IEnumerable<IModListingGetter>` -- static list of mod listings
- `IEnumerable<ModKey>` -- static list of ModKeys
- `ILoadOrderGetter` -- load order getter interface

---

### Class: ModKeyPicker

Concrete single-selection ModKey picker. Extends `AModKeyPicker`.

**Additional Dependency Properties:**

| Property | Type | Default | Description |
|---|---|---|---|
| `MaxSearchBoxHeight` | `double` | 1000 | Maximum height of the search results dropdown |
| `SearchBoxHeight` | `double` | NaN | Explicit height of the search results dropdown |

**Behavior:** Sets `ModKey` from click and exits search mode.

---

### Class: ModKeyMultiPicker

Multi-selection ModKey picker with an editable list. Extends `AModKeyPicker`.

**Template Parts:**
- `PART_AddedModKeyListBox` (ListBox) -- displays selected ModKeys with drag-and-drop reordering

**Additional Dependency Properties:**

| Property | Type | Description |
|---|---|---|
| `ModKeys` | `IList<ModKey>?` | Backing list of selected ModKey values |
| `SelectedModKey` | `ModKey?` | Currently selected ModKey in the list (TwoWay) |
| `SelectedModKeyViewModel` | `SelectedVm<ModKey>?` | Selection wrapper VM (TwoWay) |
| `AddModKeyCommand` | `ICommand` | Adds the current `ModKey` to `ModKeys` |
| `DeleteSelectedItemsCommand` | `ICommand` | Removes selected items from `ModKeys` |
| `SelectedForegroundBrush` | `Brush` | Foreground brush for selected items |
| `ItemHoverBrush` | `Brush` | Brush for hovered items |
| `SelectedBackgroundBrush` | `Brush` | Background brush for selected items |

**Public Properties:**

```csharp
public IDerivativeSelectedCollection<ModKey> ModKeySelectionViewModels { get; }
```

---

### Class: ModKeyBox

Text-entry control for ModKey values with file extension selection. Extends `NoggogControl`.

**Template Parts:**
- `PART_FileNameBox` (TextBox) -- text input for the mod file name

**Dependency Properties:**

| Property | Type | Description |
|---|---|---|
| `ModKey` | `ModKey` | The ModKey value (TwoWay) |
| `FileName` | `string` | File name portion without extension (TwoWay, EditorBrowsable: Never) |
| `ModType` | `ModType` | The mod type/extension (TwoWay, EditorBrowsable: Never) |
| `Watermark` | `string` | Placeholder text (default: "Mod name") |
| `Error` | `ErrorResponse` | ReadOnly validation error state |
| `MaxSearchBoxHeight` | `double` | Maximum height of search results (default: 1000) |
| `SearchBoxHeight` | `double` | Explicit search box height (default: NaN) |

**Public Properties:**

```csharp
public IEnumerable<ModType> ModTypes => Enums<ModType>.Values;
```

**Behavior:** Bidirectional sync between `ModKey` and `FileName`/`ModType`:
- Handles both name-with-extension and name-without-extension inputs.
- On focus, auto-selects the name portion (excluding extension).

---

## Mutagen.Bethesda.WPF.Plugins.Converters

### Class: CanLookupConverter

`IMultiValueConverter` that checks if a FormKey can be resolved in a link cache.

**Binding Inputs (3 required):**
1. `FormKey` -- the FormKey to look up
2. `ILinkCache` -- the link cache
3. `Type` or `IEnumerable<Type>` -- scoped record types

**Parameter:** Optional `bool` or `"FALSE"` string to invert the result.

**Returns:** `true`/`false` based on whether the FormKey resolves.

---

### Class: CanLookupVisibilityConverter

`IMultiValueConverter` that returns `Visibility.Visible` or `Visibility.Collapsed` based on FormKey resolution.

**Binding Inputs:** Same as `CanLookupConverter`.

**Returns:** `Visibility.Visible` if resolved (or `Visibility.Collapsed` if parameter inverts).

---

### Class: FormKeyEditorIdLookupConverter

`IMultiValueConverter` that resolves a FormKey to its EditorID string.

**Binding Inputs (3 required):**
1. `FormKey`
2. `ILinkCache`
3. `Type` or `IEnumerable<Type>`

**Returns:**
- The EditorID string if found
- `"<No Editor ID>"` if resolved but EditorID is empty
- `"<Not Found>"` if not resolved (customizable via parameter)

---

### Class: FormKeyLookupConverter

`IMultiValueConverter` that resolves a FormKey to either its EditorID or its string representation.

**Binding Inputs (3 required):**
1. `FormKey`
2. `ILinkCache`
3. `Type` or `IEnumerable<Type>`

**Returns:**
- The EditorID if found and non-empty
- `FormKey.ToString()` otherwise
- Custom fail message via parameter

---

### Class: ModTypeStringConverter

`IValueConverter` that converts between `ModType` enum and its file extension string.

```csharp
// Convert: ModType -> string (file extension)
// ConvertBack: string -> ModType (via ModKey.TryConvertExtensionToType)
```

---

### Class: RecordTypeGameConverter

`IValueConverter` that converts a record `Type` to its game namespace string using the Loqui registration system.

```csharp
// Convert: Type -> string (ProtocolKey.Namespace)
// ConvertBack: not supported
```

**Static Constructor:** Calls `Warmup.Init()` to ensure Loqui registrations are loaded.

---

### Class: RecordTypeNameConverter

`IValueConverter` that converts a record `Type` to its human-readable class name.

```csharp
// Convert: Type -> string (registered ClassType.Name or fallback Type.Name)
// ConvertBack: not supported
```

**Static Constructor:** Calls `Warmup.Init()` to ensure Loqui registrations are loaded.

---

## Mutagen.Bethesda.WPF.Plugins.Order

### Interface: ILoadOrderVM

Interface for load order view models.

```csharp
public interface ILoadOrderVM
{
    bool ShowDisabled { get; }
    bool ShowGhosted { get; }
    IEnumerable LoadOrder { get; }
}
```

---

### Class: ALoadOrderVM\<TEntryVM\>

Abstract base class for load order view models. Extends `ViewModel`, implements `ILoadOrderVM`.

**Type Constraint:** `TEntryVM : IModListingGetter`

**Properties:**

```csharp
[Reactive] public bool ShowDisabled { get; set; }
[Reactive] public bool ShowGhosted { get; set; }
public abstract IObservableCollection<TEntryVM> LoadOrder { get; }
```

---

### Class: ReadOnlyModListingVM

Read-only view model wrapping an `ILoadOrderListingGetter` with reactive file existence monitoring. Extends `ViewModel`, implements `IModListingGetter`.

```csharp
public class ReadOnlyModListingVM : ViewModel, IModListingGetter
{
    public ModKey ModKey { get; }
    public bool Enabled { get; }
    public bool Ghosted { get; }
    public string GhostSuffix { get; }
    public string FileName { get; }
    public bool ModExists { get; }  // reactive, watches file system

    public ReadOnlyModListingVM(ILoadOrderListingGetter listing, string dataFolder)
}
```

---

### Class: LoadOrderListingView

WPF UserControl for displaying a single load order listing with an enable/disable checkbox. Uses ReactiveUI `WhenActivated` for view-ViewModel binding.

**Base:** `NoggogUserControl<IModListingGetter>`

**Behavior:**
- Displays `ModKey` as checkbox content
- Checkbox is enabled only for `IModListing` (mutable) items
- Two-way binds the `Enabled` property for mutable listings

---

### Class: LoadOrderView

WPF UserControl for displaying a full load order list. Uses ReactiveUI `WhenActivated` for binding.

**Base:** `NoggogUserControl<ILoadOrderVM>`

**Behavior:** Binds `ViewModel.LoadOrder` to an `ItemsControl.ItemsSource`.

---

## Mutagen.Bethesda.WPF.Plugins.Order.Implementations

### Class: FileSyncedLoadOrderListingVM

Editable load order listing view model that monitors the file system for mod existence. Extends `ViewModel`, implements `IModListing`.

```csharp
public class FileSyncedLoadOrderListingVM : ViewModel, IModListing
{
    public ModKey ModKey { get; }
    [Reactive] public bool Enabled { get; set; }
    [Reactive] public string GhostSuffix { get; set; }
    public bool ModExists { get; }   // reactive file watcher
    public bool Ghosted { get; }     // derived from GhostSuffix
    public string FileName { get; }  // derived from ModKey + GhostSuffix

    public FileSyncedLoadOrderListingVM(
        IDataDirectoryProvider dataDirectoryContext,
        ILoadOrderListingGetter listing)
}
```

---

### Class: FileSyncedLoadOrderVM

Load order view model that live-syncs changes back to the plugins file. Extends `ALoadOrderVM<FileSyncedLoadOrderListingVM>`.

```csharp
public class FileSyncedLoadOrderVM : ALoadOrderVM<FileSyncedLoadOrderListingVM>
{
    public ErrorResponse State { get; }  // reactive load state
    public override IObservableCollection<FileSyncedLoadOrderListingVM> LoadOrder { get; }

    public FileSyncedLoadOrderVM(
        IPluginLiveLoadOrderProvider liveLoadOrderProvider,
        ILoadOrderWriter writer,
        IPluginListingsPathContext pluginPathContext,
        IDataDirectoryProvider dataDirectoryContext)
}
```

**Behavior:** Monitors `Enabled` and `GhostSuffix` changes on listings and auto-saves back to the plugin file. Changes are throttled at 500ms and only written when the sequence actually changes (using a custom `SequenceEqualityComparer`).

---

## Mutagen.Bethesda.WPF.Reflection

### Static Class: ReflectionMixIn

Extension methods for reflection-based attribute lookup by name (string matching rather than strong typing). Used by the settings system to discover custom attributes without requiring direct type references.

```csharp
public static class ReflectionMixIn
{
    // Gets a property value from a named attribute, with fallback
    public static T GetCustomAttributeValueByName<T>(
        this MemberInfo info, string attrName, string valName, T fallback)

    // Tries to find a named attribute on a member
    public static bool TryGetCustomAttributeByName(
        this MemberInfo info, string name, out Attribute attr)

    // Returns all attributes matching the given name
    public static IEnumerable<Attribute> GetCustomAttributesByName(
        this MemberInfo info, string name)

    // Checks if an attribute (or its base types) matches a name
    public static bool IsNamed(Attribute a, string name)

    // Checks if a type (or its base types) matches a name
    public static bool IsNamed(Type type, string name)
}
```

---

### Record: ReflectionSettingsParameters

Immutable parameter record passed through the reflection-based settings tree during construction.

```csharp
public record ReflectionSettingsParameters(
    Assembly Assembly,
    IObservable<IChangeSet<IModListingGetter>> DetectedLoadOrder,
    IObservable<ILinkCache?> LinkCache,
    Type TargetType,
    object? DefaultVal,
    ReflectionSettingsVM MainVM,
    SettingsNodeVM? Parent)
```

**Factory Methods:**

```csharp
// From a Type with observable parameters
public static ReflectionSettingsParameters FromType(
    IObservable<IChangeSet<IModListingGetter>> detectedLoadOrder,
    IObservable<ILinkCache?> linkCache,
    Type type,
    object? defaultVal = null)

// Generic version
public static ReflectionSettingsParameters FromType<TType>(
    IObservable<IChangeSet<IModListingGetter>> detectedLoadOrder,
    IObservable<ILinkCache?> linkCache,
    TType? defaultVal = null) where TType : class

// From default value (infers type)
public static ReflectionSettingsParameters CreateFrom<TType>(
    TType defaultVal,
    IObservable<IChangeSet<IModListingGetter>> detectedLoadOrder,
    IObservable<ILinkCache?> linkCache) where TType : class

// Static enumerable/link cache overloads
public static ReflectionSettingsParameters FromType<TType>(
    IEnumerable<IModListingGetter> detectedLoadOrder,
    ILinkCache? linkCache,
    TType? defaultVal = null) where TType : class

public static ReflectionSettingsParameters CreateFrom<TType>(
    TType defaultVal,
    IEnumerable<IModListingGetter> detectedLoadOrder,
    ILinkCache? linkCache) where TType : class
```

---

### Class: ReflectionSettingsVM

Root view model for reflection-based auto-generated settings UI. Extends `ViewModel`.

```csharp
public class ReflectionSettingsVM : ViewModel
{
    public ObjectSettingsVM ObjVM { get; }

    [Reactive] public SettingsNodeVM SelectedSettings { get; set; }
    [Reactive] public SettingsNodeVM? ScrolledToSettings { get; set; }

    public ReflectionSettingsVM(ReflectionSettingsParameters param)
}
```

**Behavior:** Creates an `ObjectSettingsVM` from the given parameters, wrapping the target type's settings. Supports breadcrumb-style navigation via `SelectedSettings` and `ScrolledToSettings`.

---

### Class: AutogeneratedSettingView

WPF UserControl for displaying auto-generated settings. Uses ReactiveUI activation.

**Base:** `NoggogUserControl<ReflectionSettingsVM>`

**Behavior:** Binds `ViewModel.SelectedSettings` to a `MainGrid.DataContext` for rendering the currently selected settings node.

---

### Class: SettingDepthView

WPF UserControl for displaying the breadcrumb navigation path (parent chain) of a settings node.

**Base:** `NoggogUserControl<SettingsNodeVM>`

**Behavior:** Binds the parent list from `ViewModel.Parents.Value` to a `ParentSettingList.ItemsSource`.

---

## Mutagen.Bethesda.WPF.Reflection.Fields

### Record: FieldMeta

Metadata record for a single settings field in the reflection tree.

```csharp
public record FieldMeta(
    string DisplayName,    // Human-readable name (Humanized from member name)
    string DiskName,       // Serialization key name
    string? Tooltip,       // Optional tooltip text
    ReflectionSettingsVM MainVM,   // Root settings VM reference
    SettingsNodeVM? Parent,        // Parent node in the tree
    bool IsPassthrough)            // Whether this node is a pass-through container

{
    public static readonly FieldMeta Empty;
}
```

---

### Abstract Class: SettingsNodeVM

Base class for all reflection-based settings nodes. Extends `ViewModel`.

```csharp
public abstract class SettingsNodeVM : ViewModel
{
    public FieldMeta Meta { get; set; }
    public Lazy<IEnumerable<SettingsNodeVM>> Parents { get; }
    public ICommand FocusSettingCommand { get; }
    public bool IsFocused { get; }

    // Discovers properties/fields on the target type
    public static IEnumerable<MemberInfo> GetMemberInfos(ReflectionSettingsParameters param)

    // Creates SettingsNodeVM array for all discovered members
    public static SettingsNodeVM[] Factory(ReflectionSettingsParameters param)

    // Creates a single SettingsNodeVM for a member (type-switching factory)
    public static SettingsNodeVM MemberFactory(
        ReflectionSettingsParameters param, MemberInfo? member)

    // Gets the serialization name (from JsonDiskName attribute or member name)
    public static string GetDiskName(MemberInfo? member)

    // Gets the display name (from SettingName attribute or humanized member name)
    public static string GetDisplayName(MemberInfo? member)

    // Import from JSON
    public abstract void Import(JsonElement property, Action<string> logger);

    // Persist to JObject
    public abstract void Persist(JObject obj, Action<string> logger);

    // Deep-clone this node
    public abstract SettingsNodeVM Duplicate();

    // Post-construction hook
    public virtual void WrapUp();
}
```

**MemberFactory Type Mapping:**

| .NET Type | SettingsNodeVM Created |
|---|---|
| `Boolean` | `BoolSettingsVM` |
| `SByte` | `Int8SettingsVM` |
| `Int16` | `Int16SettingsVM` |
| `Int32` | `Int32SettingsVM` |
| `Int64` | `Int64SettingsVM` |
| `Byte` | `UInt8SettingsVM` |
| `UInt16` | `UInt16SettingsVM` |
| `UInt32` | `UInt32SettingsVM` |
| `UInt64` | `UInt64SettingsVM` |
| `Double` | `DoubleSettingsVM` |
| `Single` | `FloatSettingsVM` |
| `Decimal` | `DecimalSettingsVM` |
| `String` | `StringSettingsVM` |
| `ModKey` | `ModKeySettingsVM` |
| `FormKey` | `FormKeySettingsVM` |
| `Array<T>`, `List<T>`, `IEnumerable<T>`, `HashSet<T>` | `Enumerable*SettingsVM` (by element type) |
| `Dictionary<string, T>` | `DictionarySettingsVM` |
| `Dictionary<TEnum, T>` | `EnumDictionarySettingsVM` |
| FormLink types | `FormLinkSettingsVM` |
| Enum types | `EnumSettingsVM` |
| Other objects | `ObjectSettingsVM` |
| Unknown | `UnknownSettingsVM` |

**Attribute Support (matched by name):**
- `Ignore` -- excludes a member from the settings UI
- `MaintainOrder` -- controls display ordering
- `SettingName` -- overrides the display name
- `JsonDiskName` -- overrides the serialization key
- `Tooltip` -- provides tooltip text
- `StaticEnumDictionary` -- marks enum dictionaries as static
- `ObjectNameMember` -- specifies which members provide a display name for objects
- `FormLinkPickerCustomization` -- specifies scoped types for FormLink pickers

---

### Interface: IBasicSettingsNodeVM

Interface for simple value-bearing settings nodes.

```csharp
public interface IBasicSettingsNodeVM : INotifyPropertyChanged
{
    string DisplayName { get; }
    object Value { get; }
    bool IsSelected { get; set; }
    void WrapUp();
}
```

---

### Abstract Class: BasicSettingsVM\<T\>

Generic base class for simple typed settings. Extends `SettingsNodeVM`, implements `IBasicSettingsNodeVM`.

```csharp
public abstract class BasicSettingsVM<T> : SettingsNodeVM, IBasicSettingsNodeVM
{
    public T DefaultValue { get; }
    [Reactive] public T Value { get; set; }
    [Reactive] public bool IsSelected { get; set; }
    public string DisplayName { get; }  // derived from Value.ToString()

    public abstract T Get(JsonElement property);
    public abstract T GetDefault();
}
```

---

### Concrete BasicSettingsVM Implementations

All follow the same pattern: parameterized constructor, default constructor, `Get()`, `GetDefault()`, and `Duplicate()`.

| Class | Type `T` | JSON Accessor |
|---|---|---|
| `BoolSettingsVM` | `bool` | `GetBoolean()` |
| `StringSettingsVM` | `string` | `GetString()` |
| `Int8SettingsVM` | `sbyte` | `GetSByte()` |
| `Int16SettingsVM` | `short` | `GetInt16()` |
| `Int32SettingsVM` | `int` | `GetInt32()` |
| `Int64SettingsVM` | `long` | `GetInt64()` |
| `UInt8SettingsVM` | `byte` | `GetByte()` |
| `UInt16SettingsVM` | `ushort` | `GetUInt16()` |
| `UInt32SettingsVM` | `uint` | `GetUInt32()` |
| `UInt64SettingsVM` | `ulong` | `GetUInt64()` |
| `DoubleSettingsVM` | `double` | `GetDouble()` |
| `FloatSettingsVM` | `float` | `GetSingle()` |
| `DecimalSettingsVM` | `decimal` | `GetDecimal()` |

---

### Class: FormKeySettingsVM

Settings node for `FormKey` values. Extends `BasicSettingsVM<FormKey>`.

```csharp
public class FormKeySettingsVM : BasicSettingsVM<FormKey>
{
    public override FormKey Get(JsonElement property)
    public override FormKey GetDefault() => FormKey.Null

    // Static helpers for import/persist/origin stripping
    public static FormKey Import(JsonElement property)
    public static string Persist(FormKey formKey)
    public static FormKey StripOrigin(FormKey formKey)
    public static FormKey? TryStripOrigin(object? o)
}
```

---

### Class: ModKeySettingsVM

Settings node for `ModKey` values. Extends `BasicSettingsVM<ModKey>`.

```csharp
public class ModKeySettingsVM : BasicSettingsVM<ModKey>
{
    public IObservable<IChangeSet<ModKey>> DetectedLoadOrder { get; }

    public ModKeySettingsVM(
        IObservable<IChangeSet<ModKey>> detectedLoadOrder,
        FieldMeta fieldMeta,
        object? defaultVal)

    // Static helpers
    public static ModKey Import(JsonElement property)
    public static string Persist(ModKey modKey)
    public static ModKey? TryStripOrigin(object? o)
}
```

---

### Class: FormLinkSettingsVM

Settings node for FormLink values with scoped type resolution and live link cache integration. Extends `SettingsNodeVM`, implements `IBasicSettingsNodeVM`.

```csharp
public class FormLinkSettingsVM : SettingsNodeVM, IBasicSettingsNodeVM
{
    public ILinkCache? LinkCache { get; }      // reactive
    [Reactive] public FormKey Value { get; set; }
    public IEnumerable<Type> ScopedTypes { get; }
    [Reactive] public bool IsSelected { get; set; }
    [Reactive] public Type? ValueType { get; set; }
    public string DisplayName { get; }         // resolved EditorID or FormKey string

    public static FormLinkSettingsVM Factory(
        IObservable<ILinkCache?> linkCache,
        FieldMeta fieldMeta,
        Type[] targetTypes,
        object? defaultVal)
}
```

---

### Class: EnumSettingsVM

Settings node for enum values with a dropdown of available names. Extends `SettingsNodeVM`, implements `IBasicSettingsNodeVM`.

```csharp
public class EnumSettingsVM : SettingsNodeVM, IBasicSettingsNodeVM
{
    public IEnumerable<string> EnumNames { get; }
    [Reactive] public string Value { get; set; }
    [Reactive] public bool IsSelected { get; set; }
    public string DisplayName { get; }

    public static EnumSettingsVM Factory(
        FieldMeta fieldMeta, object? defaultVal, Type enumType)
}
```

---

### Class: ObjectSettingsVM

Settings node for complex object types with child nodes. Extends `SettingsNodeVM`.

```csharp
public class ObjectSettingsVM : SettingsNodeVM
{
    public ObservableCollection<SettingsNodeVM> Nodes { get; }
    public IObservableCollection<string>? Names { get; }  // lazy name tracking

    // Constructor from reflection
    public ObjectSettingsVM(ReflectionSettingsParameters param, FieldMeta fieldMeta)

    // Constructor from pre-built nodes
    public ObjectSettingsVM(
        FieldMeta fieldMeta,
        Dictionary<string, SettingsNodeVM> nodes,
        string[] names)

    // Static import/persist helpers
    public static void ImportStatic(
        Dictionary<string, SettingsNodeVM> nodes,
        JsonElement root,
        Action<string> logger)

    public static void PersistStatic(
        Dictionary<string, SettingsNodeVM> nodes,
        string? name,
        JObject obj,
        Action<string> logger)
}
```

---

### Class: UnknownSettingsVM

Fallback settings node for unrecognized types. Logs import attempts and produces no output on persist.

```csharp
public class UnknownSettingsVM : SettingsNodeVM
{
    public UnknownSettingsVM(FieldMeta fieldMeta)
}
```

---

### Class: UnknownBasicSettingsVM

Sentinel implementation of `IBasicSettingsNodeVM` for unrecognized basic values.

```csharp
public class UnknownBasicSettingsVM : ViewModel, IBasicSettingsNodeVM
{
    public static readonly UnknownBasicSettingsVM Empty;
    public object Value => "Unknown";
    public string DisplayName => "Unknown";
}
```

---

### Class: ListElementWrapperVM\<TItem, TWrapper\>

Generic wrapper for list items that adds selection tracking. Used by enumerable settings VMs. Extends `ViewModel`, implements `IBasicSettingsNodeVM`.

```csharp
public class ListElementWrapperVM<TItem, TWrapper> : ViewModel, IBasicSettingsNodeVM
    where TWrapper : IBasicSettingsNodeVM
{
    public TWrapper Value { get; }
    [Reactive] public bool IsSelected { get; set; }
    public string DisplayName { get; }  // derived from Value.DisplayName
}
```

---

### Class: DictionarySettingItemVM

View model for a single key-value pair in a dictionary settings node.

```csharp
public class DictionarySettingItemVM
{
    public string Key { get; }
    public SettingsNodeVM Value { get; }
}
```

---

### Abstract Class: ADictionarySettingsVM

Base class for dictionary-type settings nodes. Extends `SettingsNodeVM`.

```csharp
public abstract class ADictionarySettingsVM : SettingsNodeVM
{
    public ObservableCollection<DictionarySettingItemVM> Items { get; }
    [Reactive] public DictionarySettingItemVM? Selected { get; set; }

    // Helper for extracting default values via reflection
    public static Dictionary<string, object> GetDefaultValDictionary(object? defaultVals)
}
```

---

### Class: DictionarySettingsVM

Editable string-keyed dictionary settings with add/delete commands. Extends `ADictionarySettingsVM`.

```csharp
public class DictionarySettingsVM : ADictionarySettingsVM
{
    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ConfirmCommand { get; }
    [Reactive] public string AddPaneText { get; set; }
    [Reactive] public bool MidDelete { get; set; }

    public static DictionarySettingsVM Factory(
        ReflectionSettingsParameters param, FieldMeta fieldMeta)
}
```

---

### Class: EnumDictionarySettingsVM

Static enum-keyed dictionary settings (keys are fixed enum values). Extends `ADictionarySettingsVM`.

```csharp
public class EnumDictionarySettingsVM : ADictionarySettingsVM
{
    public static EnumDictionarySettingsVM Factory(
        ReflectionSettingsParameters param,
        FieldMeta fieldMeta,
        Type enumType)
}
```

---

### Abstract Class: EnumerableSettingsVM

Base class for list/collection settings nodes with add/delete commands. Extends `SettingsNodeVM`.

```csharp
public abstract class EnumerableSettingsVM : SettingsNodeVM
{
    public ObservableCollection<IBasicSettingsNodeVM> Values { get; }
    public ReactiveCommand<Unit, Unit> AddCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    [Reactive] public IList? SelectedValues { get; set; }
}
```

---

### Enumerable Settings Implementations

| Class | Element Type | Description |
|---|---|---|
| `EnumerableNumericSettingsVM` | Numeric types | Factory creates typed wrappers for all numeric BasicSettingsVM types |
| `EnumerableStringSettingsVM` | `string` | List of string values |
| `EnumerableEnumSettingsVM` | Enum values | List of enum value selections |
| `EnumerableFormKeySettingsVM` | `FormKey` | List of FormKey values with strip-origin support |
| `EnumerableModKeySettingsVM` | `ModKey` | List of ModKey values with detected load order |
| `EnumerableFormLinkSettingsVM` | FormLink | List of FormLink values with link cache and scoped types |
| `EnumerableObjectSettingsVM` | Complex objects | List of nested `ObjectSettingsVM` instances |

---

### Class: EnumerableObjectSettingsVM

Specialized enumerable for complex object types. Extends `SettingsNodeVM` directly (not `EnumerableSettingsVM`).

```csharp
public class EnumerableObjectSettingsVM : SettingsNodeVM
{
    public class SelectionWrapper
    {
        [Reactive] public bool IsSelected { get; set; }
        public ObjectSettingsVM Value { get; set; }
    }

    public ObservableCollection<SelectionWrapper> Values { get; }
    public ReactiveCommand<Unit, Unit> AddCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    [Reactive] public IList? SelectedValues { get; set; }

    public static EnumerableObjectSettingsVM Factory(
        ReflectionSettingsParameters param, FieldMeta fieldMeta)
}
```

---

### Class: EnumerableFormLinkSettingsVM

Specialized enumerable for FormLink values with link cache integration. Extends `SettingsNodeVM` directly.

```csharp
public class EnumerableFormLinkSettingsVM : SettingsNodeVM
{
    public ObservableCollection<FormKey> Values { get; }
    public ILinkCache? LinkCache { get; }       // reactive
    public IEnumerable<Type> ScopedTypes { get; }

    public static SettingsNodeVM Factory(
        ReflectionSettingsParameters param,
        FieldMeta fieldMeta,
        string typeName,
        object? defaultVal)
}
```

---

### Class: EnumerableModKeySettingsVM

Specialized enumerable for ModKey values with detected load order. Extends `SettingsNodeVM` directly.

```csharp
public class EnumerableModKeySettingsVM : SettingsNodeVM
{
    public ObservableCollection<ModKey> Values { get; }
    public IObservable<IChangeSet<ModKey>> DetectedLoadOrder { get; }

    public static EnumerableModKeySettingsVM Factory(
        ReflectionSettingsParameters param,
        FieldMeta fieldMeta,
        object? defaultVal)
}
```

---

## XAML Resources

### Everything.xaml

Root resource dictionary that merges all sub-resource dictionaries for the library.

### Plugins/Controls.xaml

Control templates and styles for the FormKey/ModKey picker controls.

### Plugins/Converters/Converters.xaml

Resource dictionary registering all value converters as static resources.

### Reflection Views

| XAML File | Description |
|---|---|
| `AutogeneratedSettingView.xaml` | Root view for reflection-generated settings |
| `SettingDepthView.xaml` | Breadcrumb navigation for settings depth |
| `BasicSettingsNodeView.xaml` | Template for simple value settings |
| `BoolSettingsNodeView.xaml` | Template for boolean checkbox settings |
| `EnumSettingsNodeView.xaml` | Template for enum dropdown settings |
| `ObjectSettingsNodeView.xaml` | Template for nested object settings |
| `DictionarySettingsNodeView.xaml` | Template for dictionary settings |
| `StaticEnumDictionaryView.xaml` | Template for static enum-keyed dictionaries |
| `FormLinkSettingsView.xaml` | Template for FormLink picker settings |
| `ModKeySettingsView.xaml` | Template for ModKey picker settings |
| `EnumerableSimpleSettingsNodeView.xaml` | Template for simple value lists |
| `EnumerableObjectSettingsNodeView.xaml` | Template for object lists |
| `EnumerableFormLinkSettingsNodeView.xaml` | Template for FormLink lists |
| `EnumerableModKeySettingsNodeView.xaml` | Template for ModKey lists |
| `SettingsNodeView.xaml` | DataTemplate selector for routing settings nodes to views |
| `SelectionWrapper.xaml` | Template for list item selection wrappers |
| `UnknownSettingsNodeView.xaml` | Template for unrecognized settings types |
| `Styles.xaml` | Shared styles for the reflection settings UI |
