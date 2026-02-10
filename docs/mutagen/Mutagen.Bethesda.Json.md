# Mutagen.Bethesda.Json

Provides Newtonsoft.Json converters for serializing and deserializing core Mutagen types (`ModKey`, `FormKey`, `FormLink`, and related types) to and from JSON.

**Namespace:** `Mutagen.Bethesda.Json`

**Dependencies:** `Newtonsoft.Json`, `Loqui`, `Noggog.CSharpExt`, `Mutagen.Bethesda.Kernel`

---

## Namespace: Mutagen.Bethesda.Json

### Static Class: JsonConvertersMixIn

A convenience class providing singleton converter instances and an extension method for bulk registration.

```csharp
public static class JsonConvertersMixIn
```

#### Static Fields

| Field | Type | Description |
|-------|------|-------------|
| `ModKey` | `ModKeyJsonConverter` | Singleton converter for `ModKey` serialization. |
| `FormKey` | `FormKeyJsonConverter` | Singleton converter for `FormKey` and `FormLink` serialization. |

#### Extension Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `AddMutagenConverters` | `public static void AddMutagenConverters(this JsonSerializerSettings settings)` | Registers both `ModKeyJsonConverter` and `FormKeyJsonConverter` on the given `JsonSerializerSettings` instance. |

#### Usage

```csharp
var settings = new JsonSerializerSettings();
settings.AddMutagenConverters();

// Now ModKey and FormKey types will serialize/deserialize correctly
var json = JsonConvert.SerializeObject(myModKey, settings);
```

---

### Class: ModKeyJsonConverter

A `JsonConverter` that handles serialization of `ModKey` and `ModKey?` values. ModKeys are serialized as their filename string representation (e.g., `"Skyrim.esm"`).

```csharp
public sealed class ModKeyJsonConverter : JsonConverter
```

**Inherits:** `Newtonsoft.Json.JsonConverter`

#### Supported Types

- `ModKey`
- `ModKey?`

#### Serialization Behavior

| Scenario | JSON Output |
|----------|-------------|
| `ModKey` with name `"Skyrim"` and type `Master` | `"Skyrim.esm"` |
| `ModKey.Null` | Read as `ModKey.Null` when target type is `ModKey` |
| `null` JSON value | Read as `null` when target type is `ModKey?` |

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `CanConvert` | `public override bool CanConvert(Type objectType)` | Returns `true` for `ModKey` and `ModKey?` types. |
| `ReadJson` | `public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)` | Deserializes a JSON string to a `ModKey` using `ModKey.FromNameAndExtension`. Returns `ModKey.Null` for null values when the target is non-nullable. |
| `WriteJson` | `public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)` | Serializes a `ModKey` as its `FileName` string. |

---

### Class: FormKeyJsonConverter

A `JsonConverter` that handles serialization of `FormKey`, `FormKey?`, `FormLink<T>`, `FormLinkNullable<T>`, `FormLinkInformation`, and any `IFormLinkGetter` implementations.

```csharp
public sealed class FormKeyJsonConverter : JsonConverter
```

**Inherits:** `Newtonsoft.Json.JsonConverter`

#### Supported Types

- `FormKey`
- `FormKey?`
- `FormLinkInformation`
- Any type implementing `IFormLinkGetter`

#### Serialization Format

FormKeys are serialized as their standard string representation. FormLinks include type information in angle brackets:

| Value | JSON Representation |
|-------|-------------------|
| `FormKey` | `"123456:Skyrim.esm"` |
| `FormLink<IWeaponGetter>` | `"123456:Skyrim.esm<Skyrim.Weapon>"` (simplified type name) |
| `FormLinkInformation` | `"123456:Skyrim.esm<Skyrim.Weapon>"` (with type annotation) |
| Null/empty | Handled per target type (null, `FormKey.Null`, or null link) |

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `CanConvert` | `public override bool CanConvert(Type objectType)` | Returns `true` for `FormKey`, `FormKey?`, `FormLinkInformation`, and any `IFormLinkGetter` type. |
| `ReadJson` | `public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)` | Deserializes JSON to the appropriate FormKey or FormLink type. Handles generic type resolution for `FormLink<T>` and `FormLinkNullable<T>`. Uses Loqui registration for type lookup when parsing `FormLinkInformation`. |
| `WriteJson` | `public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)` | Serializes FormKey as its string representation. For `IFormLinkIdentifier`, includes simplified type information. |

#### Deserialization Details

The converter handles several complex scenarios during deserialization:

1. **Null JSON values:**
   - `FormKey` target: returns `FormKey.Null`
   - `FormKey?` target: returns `null`
   - `FormLinkNullable<T>` target: creates a null link
   - `FormLink<T>` target: creates a link to `FormKey.Null`

2. **Generic FormLink types:**
   - When the generic type argument is known from the target type, it is used directly
   - Type annotation in the string (e.g., `<Skyrim.Weapon>`) is optional when the generic argument provides the type
   - When no generic argument exists, the type must be parsed from the angle-bracket annotation in the string

3. **Type name resolution:**
   - Short names like `"Weapon"` are prefixed with `"Mutagen.Bethesda."`
   - `"MajorRecord"` is resolved to `"Mutagen.Bethesda.Plugins.Records.MajorRecord"`
   - Types are looked up via Loqui registration system

---

## Usage Example

```csharp
using Mutagen.Bethesda.Json;
using Newtonsoft.Json;

// Register converters
var settings = new JsonSerializerSettings();
settings.AddMutagenConverters();

// Serialize a ModKey
ModKey modKey = "Skyrim.esm";
string json = JsonConvert.SerializeObject(modKey, settings);
// json: "\"Skyrim.esm\""

// Deserialize a ModKey
ModKey deserialized = JsonConvert.DeserializeObject<ModKey>(json, settings);

// Serialize a FormKey
FormKey formKey = FormKey.Factory("123456:Skyrim.esm");
string formJson = JsonConvert.SerializeObject(formKey, settings);
```
