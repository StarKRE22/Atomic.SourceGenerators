# 🧩 Entity API Source Generator

> Part of the [Atomic Source Generators](../README.md) solution. Common generator infrastructure (Roslyn 4.3.0 settings, shared helpers) lives in the parent folder.

The **Entity API Source Generator** is a Roslyn incremental source generator that turns a simple C# class into strongly typed, autocompleted extension methods for **Atomic.Entities** tags and values.

Unlike the legacy `.atomic` YAML or Rider-plugin workflows, this generator works **directly inside the Unity compiler** using the `[EntityAPI]` attribute. Add the DLL to your project, mark a static class, and the extension methods appear automatically on every build.

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

The build also auto-copies the DLL to:

```
Assets/Plugins/Atomic/SourceGenerators/EntityAPIGenerator.dll
```

> 💡 **Tip:** Use `Release` configuration. The generator is built against `netstandard2.0` so Unity can load it as a Roslyn analyzer.

---

### 2. Verify the DLL in Unity

Make sure the DLL exists at the expected location:

```
Assets/Plugins/Atomic/SourceGenerators/EntityAPIGenerator.dll
```

If it wasn't copied automatically, run the build again from the solution.

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

Create a `public static partial` class and decorate it with `[EntityAPI(typeof(IEntity))]`.

The generator supports **three declaration styles** that can be mixed freely:

```csharp
using Atomic.Entities;
using UnityEngine;

[EntityAPI(typeof(IEntity))]
public static partial class PlayerAPI
{
    // Tag declarations (all equivalent — pick one)
    public static readonly Tag Alive;
    public static readonly TagKey<IEntity> Dead;

    // Value declarations — generic wrapper (type extracted automatically)
    public static readonly ValueKey<IEntity, int> Mana;
    public static readonly ValueKey<IEntity, float> Speed;

    // Value declarations — plain types (legacy style, still fully supported)
    public static readonly int Health;
    public static readonly Vector3 Position;
}
```

#### Declaration Styles

| Declaration | Namespace Required | Resolves As | Example |
|---|---|---|---|
| `Tag Name` | `Atomic.Entities` | **Tag** | `public static readonly Tag Alive;` |
| `TagKey<TContext> Name` | `Atomic.Entities` | **Tag** (context generic is ignored) | `public static readonly TagKey<IEntity> Dead;` |
| `ValueKey<TContext, TValue> Name` | `Atomic.Entities` | **Value** (uses `TValue` as the method type) | `public static readonly ValueKey<IEntity, int> Mana;` |
| Any other type | none | **Value** (uses the field type directly) | `public static readonly int Health;` |

> 💡 **Tip:** Use `ValueKey<IEntity, int>` if your project uses the Key pattern, or plain `int` if it doesn't — the generated extension methods are identical. The generator extracts the second generic argument (`int`) from `ValueKey<IEntity, int>` automatically.

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

[EntityAPI(typeof(IEntity))]
public static partial class PlayerAPI
{
    public static readonly Tag Alive;
    public static readonly TagKey<IEntity> Dead;

    public static readonly ValueKey<IEntity, int> Mana;
    public static readonly int Health;
    public static readonly float Speed;
}
```

### Generated Output

```csharp
/**
 * Code generation. Don't modify!
 **/

using Atomic.Entities;
using static Atomic.Entities.EntityKeyStore;
using System.Runtime.CompilerServices;
using UnityEngine;
using Atomic.Elements;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static partial class PlayerAPI
{
    ///Values
    private static readonly int ManaKey;   // int  (extracted from ValueKey<IEntity, int>)
    private static readonly int HealthKey; // int
    private static readonly int SpeedKey;  // float

    ///Tags
    private static readonly int AliveKey;
    private static readonly int DeadKey;

    static PlayerAPI()
    {
        ManaKey = NameToId(nameof(Mana));
        HealthKey = NameToId(nameof(Health));
        SpeedKey = NameToId(nameof(Speed));
        AliveKey = NameToId(nameof(Alive));
        DeadKey = NameToId(nameof(Dead));
    }

    ///Value Extensions

    #region Mana

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMana(this IEntity entity) => entity.GetValue<int>(ManaKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetMana(this IEntity entity, out int value) => entity.TryGetValue(ManaKey, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddMana(this IEntity entity, int value) => entity.AddValue(ManaKey, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasMana(this IEntity entity) => entity.HasValue(ManaKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DelMana(this IEntity entity) => entity.DelValue(ManaKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetMana(this IEntity entity, int value) => entity.SetValue(ManaKey, value);

    #endregion

    #region Health
    // ... same pattern for int
    #endregion

    #region Speed
    // ... same pattern for float
    #endregion

    ///Tag Extensions

    #region Alive

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasAliveTag(this IEntity entity) => entity.HasTag(AliveKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AddAliveTag(this IEntity entity) => entity.AddTag(AliveKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DelAliveTag(this IEntity entity) => entity.DelTag(AliveKey);

    #endregion

    #region Dead

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasDeadTag(this IEntity entity) => entity.HasTag(DeadKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AddDeadTag(this IEntity entity) => entity.AddTag(DeadKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DelDeadTag(this IEntity entity) => entity.DelTag(DeadKey);

    #endregion
}
```

### Tags

For every `Tag` or `TagKey<T>` field the generator creates:

| Method | Description |
|--------|-------------|
| `Has{Name}Tag` | Returns `true` if the tag is present. |
| `Add{Name}Tag` | Adds the tag. |
| `Del{Name}Tag` | Removes the tag. |

### Values

For every `ValueKey<T1, T2>` or non-`Tag`/`TagKey` field the generator creates (using the resolved value type):

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
[EntityAPI(typeof(IEntity), AggressiveInlining = false)]
public static partial class DebugPlayerAPI
{
    public static readonly int Health;
}
```

Without aggressive inlining, the generated method looks identical but without the attribute:

```csharp
public static int GetHealth(this IEntity entity) => entity.GetValue<int>(HealthKey);
```

---

## 🔥 Unsafe Mode

For maximum performance, you can generate **unsafe** value accessors. Unsafe mode uses `GetValueUnsafe<T>` and adds a `Ref{Name}` method that returns a direct reference to the value.

Enable unsafe mode for the whole class:

```csharp
[EntityAPI(typeof(IEntity), Unsafe = true)]
public static partial class PlayerAPI
{
    public static readonly int Health;
    public static readonly float Speed;
}
```

### Generated Unsafe Value Methods

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static int GetHealth(this IEntity entity) => entity.GetValueUnsafe<int>(HealthKey);

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static ref int RefHealth(this IEntity entity) => ref entity.GetValueUnsafe<int>(HealthKey);

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool TryGetHealth(this IEntity entity, out int value) => entity.TryGetValueUnsafe(HealthKey, out value);
```

### Comparison: Safe vs Unsafe

| Mode | `Get` Implementation | `Ref` Method | Use when |
|------|----------------------|--------------|----------|
| Safe | `entity.GetValue<int>(HealthKey)` | ❌ no | You need validation and safety guarantees. |
| Unsafe | `entity.GetValueUnsafe<int>(HealthKey)` | ✅ `ref int RefHealth(...)` | You have verified the value exists and want zero-overhead access. |

> ⚠️ **Warning:** `Unsafe = true` removes runtime checks. Calling `GetHealth` on an entity that does not have the value can crash or return undefined data. Only use unsafe mode in performance-critical, verified code paths.

You can also mix modes. Apply the class-level `Unsafe = true` and override individual fields back to safe using the `[Unsafe(false)]` field attribute (or vice versa). Note that the source generator currently recognizes the field-level `[Unsafe]` attribute as a per-field override.

```csharp
[EntityAPI(typeof(IEntity), Unsafe = true)]
public static partial class MixedAPI
{
    public static readonly int Health;        // unsafe
    
    [Unsafe(false)]
    public static readonly float Speed;       // safe
}
```

---

## ⚙️ Configuration Attributes

### `[EntityAPI]`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `entityType` | `Type` | — | The entity type the extensions target (`IEntity` or a derived interface). |
| `Unsafe` | `bool` | `false` | When `true`, uses unsafe value accessors and emits `Ref{Name}` methods. |
| `AggressiveInlining` | `bool` | `true` | When `true`, adds `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to every method. |

### `[Unsafe]`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `value` | `bool` | `true` | Override the class-level unsafe flag for a single field. |

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

### Generated file is not written to disk

The generator produces source **in-memory** for the compiler. You do not see a `.g.cs` file in the project unless you enable the debug define:

```
ATOMIC_OUTPUT_SOURCEGEN_FILES
```

Add this to `Edit → Project Settings → Player → Scripting Define Symbols`. Generated files will then be written to `Temp/GeneratedCode/`.

---

✅ With the **Entity API Source Generator**, you keep the speed of raw `IEntity` calls while gaining **type safety**, **IDE autocomplete**, and **zero magic constants**.

