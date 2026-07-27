# Event API Generator

A Roslyn incremental source generator that reads `[EventAPI]`-marked static classes and emits extension methods for `Atomic.Events.IEventBus` (or any bus type derived from it) based on `EventKey<>` declarations.

## Usage

Declare a static partial class with `[EventAPI]` and add static readonly fields of type `EventKey<TBus>` or `EventKey<TBus, T...>` from the `Atomic.Events` namespace:

```csharp
using Atomic.Events;

namespace Game.Gameplay
{
    [EventAPI]
    public static partial class GameEventAPI
    {
        public static readonly EventKey<IEventBus> PlayerTurnStarted = new(nameof(PlayerTurnStarted));
        public static readonly EventKey<IEventBus, IGameEntity> EntityDespawned = new(nameof(EntityDespawned));
    }
}
```

The generator produces extension methods such as:

```csharp
public static partial class GameEventAPI
{
    public static Subscription SubscribePlayerTurnStarted(this IEventBus bus, Action action)
        => bus.Subscribe(GameEventAPI.PlayerTurnStarted.Id, action);

    public static void UnsubscribePlayerTurnStarted(this IEventBus bus, Action action)
        => bus.Unsubscribe(GameEventAPI.PlayerTurnStarted.Id, action);

    public static void InvokePlayerTurnStarted(this IEventBus bus)
        => bus.Invoke(GameEventAPI.PlayerTurnStarted.Id);

    public static bool IsSubscribedPlayerTurnStarted(this IEventBus bus)
        => bus.IsSubscribed(GameEventAPI.PlayerTurnStarted.Id);

    public static bool DisposePlayerTurnStarted(this IEventBus bus)
        => bus.Dispose(GameEventAPI.PlayerTurnStarted.Id);

    public static Subscription<IGameEntity> SubscribeEntityDespawned(this IEventBus bus, Action<IGameEntity> action)
        => bus.Subscribe<IGameEntity>(GameEventAPI.EntityDespawned.Id, action);

    public static void UnsubscribeEntityDespawned(this IEventBus bus, Action<IGameEntity> action)
        => bus.Unsubscribe<IGameEntity>(GameEventAPI.EntityDespawned.Id, action);

    public static void InvokeEntityDespawned(this IEventBus bus, IGameEntity arg)
        => bus.Invoke<IGameEntity>(GameEventAPI.EntityDespawned.Id, arg);

    public static bool IsSubscribedEntityDespawned(this IEventBus bus)
        => bus.IsSubscribed(GameEventAPI.EntityDespawned.Id);

    public static bool DisposeEntityDespawned(this IEventBus bus)
        => bus.Dispose(GameEventAPI.EntityDespawned.Id);
}
```

## Supported key shapes

| Key type | Generated methods |
|----------|-------------------|
| `EventKey<TBus>` | `Subscribe`, `Unsubscribe`, `Invoke`, `IsSubscribed`, `Dispose` |
| `EventKey<TBus, T>` | `Subscribe`, `Unsubscribe`, `Invoke(T)`, `IsSubscribed`, `Dispose` |
| `EventKey<TBus, T1, T2>` | `Subscribe`, `Unsubscribe`, `Invoke(T1, T2)`, `IsSubscribed`, `Dispose` |
| `EventKey<TBus, T1, T2, T3>` | `Subscribe`, `Unsubscribe`, `Invoke(T1, T2, T3)`, `IsSubscribed`, `Dispose` |

The bus type is read from the **first generic argument** of each field, so the same API class can target different bus types if needed.

## Requirements

- Class must be `static` and declared `partial` (the generator emits a partial class with the same name).
- Fields must be `static` and of type `EventKey<>` from the `Atomic.Events` namespace.
- Each field must be initialized with a non-default constructor (`new(nameof(FieldName))` or `new(id)`).
- The `[EventAPI]` attribute is parameterless.

## Diagnostics

The [EventAPIAnalyzer](../EventAPIAnalyzer/README.md) reports errors for missing or invalid initializers.

## Unity import settings

The generator DLL is copied automatically to:

```
Assets/Plugins/Atomic/SourceGenerators/EventAPIGenerator.dll
```

Unity should import it with **Analyze Sources** enabled. If it doesn't, select the DLL in the Project view and tick **Analyze Sources** and **Process Sources** in the Inspector.

## Build

From the `SourceGenerators` directory:

```bash
dotnet build Atomic.SourceGenerators.sln -c Release
```

The generator DLL and PDB are copied to `Assets/Plugins/Atomic/SourceGenerators/` automatically.

## Implementation notes

- Targets `netstandard2.0` and `Microsoft.CodeAnalysis.CSharp` 4.3.0 for Unity 6000 compatibility.
- Uses `SyntaxProvider.CreateSyntaxProvider` for Roslyn 4.3.0 compatibility.
- Generated methods read the static field's `Id` property directly (`ClassName.FieldName.Id`).
