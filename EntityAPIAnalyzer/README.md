# Entity API Analyzer

A Roslyn diagnostic analyzer that validates `[EntityAPI]` class declarations for the [EntityAPIGenerator](../EntityAPIGenerator/README.md).

## Purpose

The Entity API generator only accepts static fields of type `ValueKey<>` or `TagKey<>`. For the generated extension methods to work correctly, each key must have a valid `Id`. This analyzer ensures every key field is initialized and never left with the default (`0`) id. Additional `[EntityAPI]`-related rules can be added here in the future.

## Rules

| ID | Severity | Description |
|----|----------|-------------|
| `EAPI0001` | Error | A `ValueKey<>` / `TagKey<>` field in an `[EntityAPI]` class has no initializer. |
| `EAPI0002` | Error | A `ValueKey<>` / `TagKey<>` field is initialized with `new()` or `default`, which leaves the id at `0`. |

## Code fixes

Both diagnostics ship with a quick fix (Ctrl+. or `Alt+Enter` in Rider/VS):

> **Initialize 'FieldName' with nameof(FieldName)** — inserts or replaces the initializer with `= new(nameof(FieldName))`.

### Before
```csharp
[EntityAPI]
public static partial class PlayerContextAPI
{
    public static readonly ValueKey<IPlayerContext, int> Health;
    public static readonly TagKey<IPlayerContext> Alive = new();
}
```

### After applying the code fix
```csharp
[EntityAPI]
public static partial class PlayerContextAPI
{
    public static readonly ValueKey<IPlayerContext, int> Health = new(nameof(Health));
    public static readonly TagKey<IPlayerContext> Alive = new(nameof(Alive));
}
```

## Valid and invalid examples

### Valid
```csharp
[EntityAPI]
public static partial class PlayerContextAPI
{
    public static readonly ValueKey<IPlayerContext, int> Health = new(nameof(Health));
    public static readonly TagKey<IPlayerContext> Alive = new("Alive");
    public static readonly ValueKey<IPlayerContext, float> Speed = new(123);
}
```

### Invalid
```csharp
[EntityAPI]
public static partial class PlayerContextAPI
{
    // EAPI0001: field is not initialized
    public static readonly ValueKey<IPlayerContext, int> Health;

    // EAPI0002: parameterless construction leaves Id at default value
    public static readonly TagKey<IPlayerContext> Alive = new();
}
```

## Deployment

The analyzer is built and deployed automatically as part of `Atomic.SourceGenerators.sln`:

```bash
dotnet build ../../SourceGenerators/Atomic.SourceGenerators.sln -c Release
```

The analyzer DLL is copied to:

```
Assets/Plugins/Atomic/SourceGenerators/EntityAPIAnalyzer.dll
```

Unity loads it alongside the source generators. Diagnostics appear in the Unity console and in the IDE.

## Implementation notes

- Targets `netstandard2.0` and `Microsoft.CodeAnalysis.CSharp` 4.3.0 for Unity 6000 compatibility.
- Uses `RegisterSyntaxNodeAction` on `FieldDeclarationSyntax`.
- Only analyzes static fields inside classes marked with `[EntityAPI]`.
- Only checks fields whose type is `ValueKey<>` or `TagKey<>` from the `Atomic.Entities` namespace.
