# 🧩 Entity API Source Generator

> Part of the [Atomic Source Generators](../README.md) solution. Common generator infrastructure (Roslyn 4.3.0 settings, shared helpers) lives in the parent folder.

The **Entity API Source Generator** is a Roslyn incremental source generator that turns a static class with `ValueKey<>` / `TagKey<>` fields into strongly typed, autocompleted extension methods for **Atomic.Entities** tags and values.

Unlike the legacy `.atomic` YAML or Rider-plugin workflows, this generator works **directly inside the Unity compiler** using the `[EntityAPI]` attribute. Add the DLL to your project, mark a static class with key fields, and the extension methods appear automatically on every build.

---

## 📑 Table of Contents

- [Requirements](#-requirements)
- [Build and Install](#-build-and-install)
  - [1. Build the Generator](#1-build-the-generator)
  - [2. Verify the DLL in Unity](#2-verify-the-dll-in-unity)
  - [3. Mark it as a Roslyn Analyzer](#3-mark-it-as-a-roslyn-analyzer)
  - [4. Configure Platform Settings](#4-configure-platform-settings)
- [Basic Usage](#-basic-usage)
  - [Declaring an API Definition](#declaring-an-api-definition)
  - [Using the Generated Extensions](#using-the-generated-extensions)
- [Generated Code](#-generated-code)
  - [Tags](#tags)
  - [Values](#values)
  - [Aggressive Inlining](#aggressive-inlining)
  - [Unsafe Mode](#unsafe-mode)
- [Configuration Attributes](#-configuration-attributes)
- [Analyzer](#-analyzer)
- [Troubleshooting](#-troubleshooting)

---

## 📝 Requirements

- **Unity 6** (6000.0 LTS or newer) — required for Roslyn source generators
- **.NET SDK 8+** or **.NET 7+** to build the generator
- The project must reference the **Atomic.Entities** runtime assembly

---

## 🔨 Build and Install

### 1. Build the Generator

Open the [Atomic Source Generators](../README.md) solution and build it in Release from the `SourceGenerators` folder:

```bash
cd SourceGenerators
dotnet build Atomic.SourceGenerators.sln -c Release
```

The compiled assembly is produced at:

```
EntityAPIGenerator/bin/Release/netstandard2.0/EntityAPIGenerator.dll
```

To copy it to your Unity project automatically, provide the destination folder:

```bash
dotnet build Atomic.SourceGenerators.sln -c Release \
  -p:AtomicDeployToUnity=true \
  -p:AtomicUnityPluginDir="C:\YourProject\Assets\Plugins\Atomic\SourceGenerators"
```

Only the `.dll` is copied (no `.pdb`). If you don't pass these properties, copy the DLL manually.

> 💡 **Tip:** Use `Release` configuration. The generator is built against `netstandard2.0` so Unity can load it as a Roslyn analyzer.

---

### 2. Verify the DLL in Unity

Make sure the DLL exists at:

```
Assets/Plugins/Atomic/SourceGenerators/EntityAPIGenerator.dll
```

If it wasn't copied, copy it manually from `bin/Release/netstandard2.0/`.

---

### 3. Mark it as a Roslyn Analyzer

Select the DLL in Unity and add the **Asset Label** `RoslynAnalyzer`.

1. Click the `EntityAPIGenerator.dll` asset in the Project window.
2. In the Inspector, find the **Asset Labels** section at the bottom.
3. Add the label: `RoslynAnalyzer`

This tells Unity's Roslyn compiler to use the DLL as a source generator during compilation.

---

### 4. Configure Platform Settings

Source generators run only at compile time and must **not** be included in the final player build.

Set the import settings exactly as shown below:

- **Auto Reference**: ✅ checked
- **Validate References**: ✅ checked
- **Select platforms for plugin**
  - **Any Platform**: ⬜ unchecked
  - **Editor**: ⬜ unchecked
  - **Standalone**: ⬜ unchecked
  - All other platforms: ⬜ unchecked

<img width="600" alt="Entity API Generator platform settings" src="Images/EntityAPIGenerator_PlatformSettings.png" />

> ⚠️ **Important:** Leaving all platforms unchecked is correct. The generator is an analyzer, not a runtime plugin.

---

## 🧩 Basic Usage

### Declaring an API Definition

Create a `public static partial` class and decorate it with `[EntityAPI]`. Declare static fields of type `ValueKey<TContext, TValue>` or `TagKey<TContext>`. The entity type is taken from the key's first generic argument, so each field can target a different entity interface if needed.

```csharp
using Atomic.Entities;
using UnityEngine;

[EntityAPI]
public static partial class PlayerAPI
{
    // Tag declarations
    public static readonly TagKey<IEntity> Alive = new(nameof(Alive));
    public static readonly TagKey<IEntity> Dead = new(nameof(Dead));

    // Value declarations
    public static readonly ValueKey<IEntity, int> Mana = new(nameof(Mana));
    public static readonly ValueKey<IEntity, float> Speed = new(nameof(Speed));
    public static readonly ValueKey<IPlayerContext, Camera> Camera = new(nameof(Camera));
}
```

#### Declaration Styles

| Declaration | Namespace Required | Entity Type | Resolves As | Example |
|---|---|---|---|---|
| `TagKey<TContext> Name` | `Atomic.Entities` | `TContext` | **Tag** | `public static readonly TagKey<IEntity> Alive = new(nameof(Alive));` |
| `TagKey Name` | `Atomic.Entities` | `IEntity` | **Tag** | `public static readonly TagKey Alive = new(nameof(Alive));` |
| `ValueKey<TContext, TValue> Name` | `Atomic.Entities` | `TContext` | **Value** (uses `TValue`) | `public static readonly ValueKey<IEntity, int> Mana = new(nameof(Mana));` |
| `ValueKey<TValue> Name` | `Atomic.Entities` | `IEntity` | **Value** (uses `TValue`) | `public static readonly ValueKey<int> Mana = new(nameof(Mana));` |

> 💡 **Tip:** Always initialize key fields (e.g. `new(nameof(Mana))`). The generated extension methods read the field's `Id` property, which is computed by the constructor. The [EntityAPIAnalyzer](../EntityAPIAnalyzer/README.md) reports missing or parameterless initializers as build errors.

> ⚠️ **Plain types and the legacy `Tag` struct are no longer supported.** Use `ValueKey<>` / `TagKey<>` for every field.

---

### Using the Generated Extensions

After the first build, the methods are available on any `IEntity`:

```csharp
IEntity entity = new Entity();

// Tags
entity.AddPlayerTag();
if (entity.HasPlayerTag())
    entity.DelPlayerTag();

// Values
entity.AddHealth(100);
int health = entity.GetHealth();
entity.SetHealth(80);

if (entity.TryGetHealth(out int current))
    Debug.Log(current);

if (entity.HasHealth())
    entity.DelHealth();

// Physics / transform
entity.SetPosition(Vector3.zero);
Vector3 pos = entity.GetPosition();
```

---

## 🔍 Generated Code

For each `[EntityAPI]` class the generator emits a matching `partial` class with the same name and namespace. You do **not** edit this file.

### Example Input

```csharp
using Atomic.Entities;
using UnityEngine;

[EntityAPI]
public static partial class PlayerAPI
{
    public static readonly TagKey<IEntity> Alive = new(nameof(Alive));
    public static readonly TagKey<IEntity> Dead = new(nameof(Dead));

    public static readonly ValueKey<IEntity, int> Mana = new(nameof(Mana));
    public static readonly ValueKey<IEntity, float> Speed = new(nameof(Speed));
    public static readonly ValueKey<IPlayerContext, Camera> Camera = new(nameof(Camera));
}
```

### Generated Output

```csharp
/**
 * Code generation. Don't modify!
 **/

using Atomic.Entities;
using System.Runtime.CompilerServices;

public static partial class PlayerAPI
{
    ///Value Extensions

    #region Mana

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMana(this IEntity entity) => entity.GetValue<int>(PlayerAPI.Mana.Id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetMana(this IEntity entity, out int value) => entity.TryGetValue(PlayerAPI.Mana.Id, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddMana(this IEntity entity, int value) => entity.AddValue(PlayerAPI.Mana.Id, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasMana(this IEntity entity) => entity.HasValue(PlayerAPI.Mana.Id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DelMana(this IEntity entity) => entity.DelValue(PlayerAPI.Mana.Id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetMana(this IEntity entity, int value) => entity.SetValue(PlayerAPI.Mana.Id, value);

    #endregion

    #region Speed
    // ... same pattern for float (extends IEntity)
    #endregion

    #region Camera
    // ... extends IPlayerContext
    #endregion

    ///Tag Extensions

    #region Alive

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasAliveTag(this IEntity entity) => entity.HasTag(PlayerAPI.Alive.Id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AddAliveTag(this IEntity entity) => entity.AddTag(PlayerAPI.Alive.Id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DelAliveTag(this IEntity entity) => entity.DelTag(PlayerAPI.Alive.Id);

    #endregion

    #region Dead
    // ... same pattern
    #endregion
}
```

### Tags

For every `TagKey` / `TagKey<T>` field the generator creates:

| Method | Description |
|--------|-------------|
| `Has{Name}Tag` | Returns `true` if the tag is present. |
| `Add{Name}Tag` | Adds the tag. |
| `Del{Name}Tag` | Removes the tag. |

### Values

For every `ValueKey<TContext, TValue>` or `ValueKey<TValue>` field the generator creates (using the resolved value type and entity type):

| Method | Description |
|--------|-------------|
| `Get{Name}` | Returns the stored value. |
| `TryGet{Name}` | Safely retrieves the value via `out` parameter. |
| `Add{Name}` | Adds the value to the entity. |
| `Has{Name}` | Returns `true` if the value exists. |
| `Del{Name}` | Removes the value. |
| `Set{Name}` | Sets the value. |

---

## ⚡ Aggressive Inlining

By default, every generated extension method is decorated with `[MethodImpl(MethodImplOptions.AggressiveInlining)]`. This removes call overhead for hot paths and keeps the generated API as fast as the direct `IEntity` calls.

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int GetHealth(this IEntity entity) => entity.GetValue<int>(HealthKey);
```

If you want to disable inlining for a specific API definition (for debugging or profiling), set the attribute property:

```csharp
[EntityAPI(AggressiveInlining = false)]
public static partial class DebugPlayerAPI
{
    public static readonly ValueKey<IEntity, int> Health = new(nameof(Health));
}
```

Without aggressive inlining, the generated method looks identical but without the attribute:

```csharp
public static int GetHealth(this IEntity entity) => entity.GetValue<int>(DebugPlayerAPI.Health.Id);
```

---

## 🔥 Unsafe Mode

For maximum performance, you can generate **unsafe** value accessors. Unsafe mode uses `GetValueUnsafe<T>` and adds a `Ref{Name}` method that returns a direct reference to the value.

Enable unsafe mode for the whole class:

```csharp
[EntityAPI(Unsafe = true)]
public static partial class PlayerAPI
{
    public static readonly ValueKey<IEntity, int> Health = new(nameof(Health));
    public static readonly ValueKey<IEntity, float> Speed = new(nameof(Speed));
}
```

### Generated Unsafe Value Methods

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int GetHealth(this IEntity entity) => entity.GetValueUnsafe<int>(PlayerAPI.Health.Id);

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static ref int RefHealth(this IEntity entity) => ref entity.GetValueUnsafe<int>(PlayerAPI.Health.Id);

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool TryGetHealth(this IEntity entity, out int value) => entity.TryGetValueUnsafe(PlayerAPI.Health.Id, out value);
```

### Comparison: Safe vs Unsafe

| Mode | `Get` Implementation | `Ref` Method | Use when |
|------|----------------------|--------------|----------|
| Safe | `entity.GetValue<int>(PlayerAPI.Health.Id)` | ❌ no | You need validation and safety guarantees. |
| Unsafe | `entity.GetValueUnsafe<int>(PlayerAPI.Health.Id)` | ✅ `ref int RefHealth(...)` | You have verified the value exists and want zero-overhead access. |

> ⚠️ **Warning:** `Unsafe = true` removes runtime checks. Calling `GetHealth` on an entity that does not have the value can crash or return undefined data. Only use unsafe mode in performance-critical, verified code paths.

You can also mix modes. Apply the class-level `Unsafe = true` and override individual fields back to safe using the `[Unsafe(false)]` field attribute (or vice versa). Note that the source generator currently recognizes the field-level `[Unsafe]` attribute as a per-field override.

```csharp
[EntityAPI(Unsafe = true)]
public static partial class MixedAPI
{
    public static readonly ValueKey<IEntity, int> Health = new(nameof(Health));        // unsafe
    
    [Unsafe(false)]
    public static readonly ValueKey<IEntity, float> Speed = new(nameof(Speed));       // safe
}
```

---

## ⚙️ Configuration Attributes

### `[EntityAPI]`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Unsafe` | `bool` | `false` | When `true`, uses unsafe value accessors and emits `Ref{Name}` methods. |
| `AggressiveInlining` | `bool` | `true` | When `true`, adds `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to every method. |

> The entity type is no longer passed to `[EntityAPI]`. It is read from each field's first generic argument (`ValueKey<TContext, TValue>` / `TagKey<TContext>`). `ValueKey<TValue>` / `TagKey` default to `IEntity`.

### `[Unsafe]`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `value` | `bool` | `true` | Override the class-level unsafe flag for a single field. |

---

## 🔬 Analyzer

Deploy the [EntityAPIAnalyzer](../EntityAPIAnalyzer/README.md) DLL alongside the generator. It reports build errors when `ValueKey<>` / `TagKey<>` fields inside `[EntityAPI]` classes are missing an initializer or are initialized with `new()` / `default`.

---

## 🔧 Troubleshooting

### Extensions are not showing in IntelliSense

1. Make sure the DLL is in `Assets/Plugins/Atomic/SourceGenerators/EntityAPIGenerator.dll`.
2. Check that the **Asset Label** is `RoslynAnalyzer`.
3. Verify platform settings: **Any Platform** must be **unchecked**, and all individual platforms must be **unchecked**.
4. Rebuild the Unity project (`Assets → Reimport All` or restart the editor).

### Build errors after adding the DLL

- Ensure the DLL is **not** included in any runtime platform. Source generators are compile-time-only analyzers.
- If you see `RS1035` warnings, they are harmless and disabled inside the generator project.
- Ensure all `[EntityAPI]` fields are `ValueKey<>` or `TagKey<>` and are initialized (e.g. `new(nameof(Field))`). The analyzer reports missing initializers.

### Generated file is not written to disk

The generator produces source **in-memory** for the compiler. You do not see a `.g.cs` file in the project unless you enable the debug define:

```
ATOMIC_OUTPUT_SOURCEGEN_FILES
```

Add this to `Edit → Project Settings → Player → Scripting Define Symbols`. Generated files will then be written to `Temp/GeneratedCode/`.

---

✅ With the **Entity API Source Generator**, you keep the speed of raw `IEntity` calls while gaining **type safety**, **IDE autocomplete**, and **zero magic constants**.

