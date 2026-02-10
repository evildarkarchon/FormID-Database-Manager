# Mutagen.Bethesda.SourceGenerators

A Roslyn incremental source generator that produces wrapper classes and extension methods for custom aspect interfaces. This generator is used internally by Mutagen to create adapter patterns that allow different record types to be accessed through shared interfaces.

**Namespace:** `Mutagen.Bethesda.SourceGenerators.CustomAspectInterface`

**Target Framework:** `netstandard2.0` (required for source generators)

**Dependencies:** `Microsoft.CodeAnalysis`, `Noggog.CSharpExt`

---

## Namespace: Mutagen.Bethesda.SourceGenerators.CustomAspectInterface

### Class: CustomAspectInterfaceGenerator

A Roslyn incremental source generator (`IIncrementalGenerator`) that scans for interfaces marked with the `[CustomAspectInterface]` attribute and generates wrapper classes plus mix-in extension methods.

```csharp
[Generator]
public class CustomAspectInterfaceGenerator : IIncrementalGenerator
```

**Implements:** `Microsoft.CodeAnalysis.IIncrementalGenerator`

**Attributes:** `[Generator]`

#### Purpose

This generator enables a pattern where:

1. An interface is decorated with `[CustomAspectInterface(typeof(ConcreteType1), typeof(ConcreteType2), ...)]`
2. The generator creates wrapper classes (e.g., `ConcreteType1Wrapper`) that implement the interface by delegating to the wrapped type's properties
3. The generator creates static extension methods (e.g., `.AsInterfaceName()`) for convenient wrapping

This allows concrete record types that share common properties but do not share a common interface in the code-generated Mutagen output to be accessed through a unified aspect interface.

#### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `Initialize` | `public void Initialize(IncrementalGeneratorInitializationContext context)` | Registers the syntax providers and source output callback. Sets up the incremental pipeline to find `[CustomAspectInterface]` attributes on interfaces and `partial class` method declarations. |
| `Execute` | `public void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<MethodDeclarationSyntax> methods, ImmutableArray<InterfaceDeclaration> interfaces)` | Performs the actual code generation. Resolves interface and type symbols, groups by namespace, and emits wrapper classes and mix-in extension methods. Output file: `CustomAspectInterfaces.g.cs`. |

#### Generated Code Structure

For each `[CustomAspectInterface]`-decorated interface, the generator produces:

**1. Wrapper Classes** (in a `#region Wrappers` block):

```csharp
// For [CustomAspectInterface(typeof(SomeRecord))]
// on interface IMyAspect { string Name { get; set; } }
public class SomeRecordWrapper : IMyAspect
{
    private readonly SomeRecord _wrapped;

    public string Name
    {
        get => _wrapped.Name;
        set => _wrapped.Name = value;
    }

    public SomeRecordWrapper(SomeRecord rhs)
    {
        _wrapped = rhs;
    }
}
```

**2. Mix-In Extension Methods** (in a `#region Mix Ins` block):

```csharp
public static class WrapperMixIns
{
    public static SomeRecordWrapper AsIMyAspect(this SomeRecord rhs)
    {
        return new SomeRecordWrapper(rhs);
    }
}
```

---

### Record: InterfaceDeclaration

Represents a parsed `[CustomAspectInterface]` attribute on an interface, before semantic analysis.

```csharp
public record InterfaceDeclaration(
    InterfaceDeclarationSyntax Interface,
    TypeOfExpressionSyntax[] Types);
```

| Property | Type | Description |
|----------|------|-------------|
| `Interface` | `InterfaceDeclarationSyntax` | The syntax node of the decorated interface. |
| `Types` | `TypeOfExpressionSyntax[]` | The `typeof(...)` arguments from the `[CustomAspectInterface]` attribute. |

---

### Record: TypeDeclaration

Pairs a `typeof(...)` syntax expression with its resolved type symbol.

```csharp
public record TypeDeclaration(
    TypeOfExpressionSyntax Syntax,
    ITypeSymbol? Symbol);
```

| Property | Type | Description |
|----------|------|-------------|
| `Syntax` | `TypeOfExpressionSyntax` | The `typeof(...)` expression from the attribute. |
| `Symbol` | `ITypeSymbol?` | The resolved type symbol, or `null` if resolution failed. |

---

### Record: InterfaceSymbolDeclaration

A fully-resolved interface declaration with its type symbol and the types it wraps.

```csharp
public record InterfaceSymbolDeclaration(
    InterfaceDeclarationSyntax Interface,
    ITypeSymbol Symbol,
    TypeDeclaration[] Types);
```

| Property | Type | Description |
|----------|------|-------------|
| `Interface` | `InterfaceDeclarationSyntax` | The interface syntax node. |
| `Symbol` | `ITypeSymbol` | The resolved type symbol for the interface. |
| `Types` | `TypeDeclaration[]` | The concrete types this interface wraps. |

---

### Record: InterfaceParameter

Represents a method parameter whose type matches a custom aspect interface.

```csharp
public record InterfaceParameter(
    InterfaceSymbolDeclaration Declaration,
    ITypeSymbol ParameterType,
    string Name);
```

| Property | Type | Description |
|----------|------|-------------|
| `Declaration` | `InterfaceSymbolDeclaration` | The interface declaration the parameter relates to. |
| `ParameterType` | `ITypeSymbol` | The parameter's type symbol. |
| `Name` | `string` | The parameter name. |

---

### Record: GenerationTarget

Represents a namespace-scoped group of interfaces and methods targeted for code generation.

```csharp
public record GenerationTarget(
    string Namespace,
    IEnumerable<InterfaceSymbolDeclaration> UsedInterfaces,
    Dictionary<MethodDeclarationSyntax, List<InterfaceParameter>> Declarations);
```

| Property | Type | Description |
|----------|------|-------------|
| `Namespace` | `string` | The namespace to generate code in. |
| `UsedInterfaces` | `IEnumerable<InterfaceSymbolDeclaration>` | The custom aspect interfaces used in this namespace. |
| `Declarations` | `Dictionary<MethodDeclarationSyntax, List<InterfaceParameter>>` | Method declarations and their interface parameter usages. |

---

### Record: MethodUsageInformation

Tracks how a method uses custom aspect interface parameters.

```csharp
public record MethodUsageInformation(
    MethodDeclarationSyntax Syntax,
    ISymbol MethodSymbol,
    List<InterfaceParameter> InterfaceUsages);
```

| Property | Type | Description |
|----------|------|-------------|
| `Syntax` | `MethodDeclarationSyntax` | The method's syntax node. |
| `MethodSymbol` | `ISymbol` | The method's resolved symbol. |
| `InterfaceUsages` | `List<InterfaceParameter>` | The custom aspect interface parameters used by this method. |
