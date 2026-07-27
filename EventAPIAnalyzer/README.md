# Event API Analyzer

A Roslyn diagnostic analyzer and code fix provider that validates `[EventAPI]` class declarations for the [EventAPIGenerator](../EventAPIGenerator/README.md).

## Purpose

The Event API generator only accepts static fields of type `EventKey<>` from the `Atomic.Events` namespace. For the generated extension methods to work correctly, each key must have a valid `Id`. This analyzer ensures every event key field is initialized and never left with the default (`0`) id.

## Rules

| ID | Severity | Description |
|----|----------|-------------|
| `EAPI0001` | Error | An `EventKey<>` field in an `[EventAPI]` class has no initializer. |
| `EAPI0002` | Error | An `EventKey<>` field is initialized with `new()` or `default`, which leaves the id at `0`. |

## Code fixes

Both diagnostics ship with a quick fix (Ctrl+. or `Alt+Enter` in Rider/VS):

> **Initialize 'FieldName' with nameof(FieldName)** — inserts or replaces the initializer with `= new(nameof(FieldName))`.

### Before
```csharp
[EventAPI]
public static partial class GameEventAPI
{
    public static readonly EventKey<IEventBus> PlayerTurnStarted;
    public static readonly EventKey<IEventBus> PlayerTurnEnded = new();
}
```

### After applying the code fix
```csharp
[EventAPI]
public static partial class GameEventAPI
{
    public static readonly EventKey<IEventBus> PlayerTurnStarted = new(nameof(PlayerTurnStarted));
    public static readonly EventKey<IEventBus> PlayerTurnEnded = new(nameof(PlayerTurnEnded));
}
```

## Deployment

The analyzer is built and deployed automatically as part of `Atomic.SourceGenerators.sln`:

```bash
dotnet build ../../SourceGenerators/Atomic.SourceGenerators.sln -c Release
```

The analyzer DLL is copied to:

```
Assets/Plugins/Atomic/SourceGenerators/EventAPIAnalyzer.dll
```

Unity loads it alongside the source generators. Diagnostics appear in the Unity console and in the IDE.

## Implementation notes

- Targets `netstandard2.0` and `Microsoft.CodeAnalysis.CSharp` 4.3.0 for Unity 6000 compatibility.
- Uses `RegisterSyntaxNodeAction` on `FieldDeclarationSyntax`.
- Only analyzes static fields inside classes marked with `[EventAPI]`.
- Only checks fields whose type is `EventKey<>` from the `Atomic.Events` namespace.
