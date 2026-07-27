# Atomic Source Generators

This folder contains the Roslyn incremental source generators for the **Atomic** framework used in the Unity project. All generators live under one solution, share common utilities, and compile into separate DLLs that Unity loads as Roslyn analyzers.

## 📁 Folder Structure

```
SourceGenerators/
├── Atomic.SourceGenerators.sln      # Solution containing all generator projects
├── Directory.Build.props            # Common MSBuild settings (Roslyn 4.3.0, netstandard2.0)
├── Atomic.SourceGenerators.Shared/  # Shared source files (no separate DLL)
│   ├── CodeWriter.cs
│   ├── DiagnosticLogger.cs
│   └── SourceOutputHelpers.cs
├── EntityAPIGenerator/              # [EntityAPI] generator for tags/values
│   ├── EntityAPIGenerator.csproj
│   ├── EntityAPIGenerator.cs
│   ├── EntityAPIParser.cs
│   ├── CodeEmitter.cs
│   ├── Models/
│   └── README.md
├── EventAPIGenerator/             # [EventAPI] generator for event keys
│   ├── EventAPIGenerator.csproj
│   ├── EventAPIGenerator.cs
│   ├── EventAPIParser.cs
│   ├── CodeEmitter.cs
│   ├── Models/
│   └── README.md
├── EntityAPIAnalyzer/             # diagnostics + code fixes for [EntityAPI]
│   ├── EntityAPIAnalyzer.csproj
│   ├── EntityAPIAnalyzer.cs
│   ├── EntityAPIDiagnostics.cs
│   ├── EntityAPICodeFixProvider.cs
│   └── README.md
└── EventAPIAnalyzer/              # diagnostics + code fixes for [EventAPI]
    ├── EventAPIAnalyzer.csproj
    ├── EventAPIAnalyzer.cs
    ├── EventAPIDiagnostics.cs
    ├── EventAPICodeFixProvider.cs
    └── README.md
```

## 🔧 Shared Code

The `Atomic.SourceGenerators.Shared` folder contains source files that are **compiled directly into each generator** via:

```xml
<ItemGroup>
  <Compile Include="..\Atomic.SourceGenerators.Shared\*.cs"
           Link="Shared\%(Filename)%(Extension)" />
</ItemGroup>
```

This keeps every generator DLL self-contained and avoids analyzer dependency issues in Unity's Roslyn pipeline.

> Do not reference a separate class-library project from the generator projects — that would create a second DLL that Unity's analyzer loader may not resolve correctly.

## 🏗️ Adding a New Generator

1. Create a new folder under `SourceGenerators/`, e.g. `MyGenerator/`.
2. Add `MyGenerator.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <AssemblyName>MyGenerator</AssemblyName>
       <RootNamespace>MyGenerator</RootNamespace>
     </PropertyGroup>
     <ItemGroup>
       <Compile Include="..\Atomic.SourceGenerators.Shared\*.cs"
                Link="Shared\%(Filename)%(Extension)" />
     </ItemGroup>
     <Target Name="CopyToUnity" AfterTargets="Build">
       <PropertyGroup>
         <_UnityPluginDir>$([MSBuild]::NormalizeDirectory('$(ProjectDir)..\..\Assets\Plugins\Atomic\SourceGenerators'))</_UnityPluginDir>
       </PropertyGroup>
       <Copy SourceFiles="$(TargetDir)$(TargetName).dll" DestinationFolder="$(_UnityPluginDir)" SkipUnchangedFiles="true" />
       <Copy SourceFiles="$(TargetDir)$(TargetName).pdb" DestinationFolder="$(_UnityPluginDir)" SkipUnchangedFiles="true" Condition="Exists('$(TargetDir)$(TargetName).pdb')" />
       <Message Text="MyGenerator deployed to $(_UnityPluginDir)" Importance="high" />
     </Target>
   </Project>
   ```
3. Add `MyGenerator.cs` with a class implementing `IIncrementalGenerator` and decorated with `[Generator]`.
4. Add the project to `Atomic.SourceGenerators.sln`.
5. Build the solution and verify the DLL appears in `Assets/Plugins/Atomic/SourceGenerators/`.

## 🛠️ Common Settings

`Directory.Build.props` centralizes:

- `TargetFramework` = `netstandard2.0` (required by Unity's compiler)
- `LangVersion` = `12`
- `Nullable` = `enable`
- `IsRoslynComponent` = `true`
- `EnforceExtendedAnalyzerRules` = `true`
- `Microsoft.CodeAnalysis.CSharp` **4.3.0** (matches Unity 6000.3.x bundled Roslyn)
- `Microsoft.CodeAnalysis.Analyzers` **3.3.4**

Generator projects only define their own `AssemblyName`, `RootNamespace`, shared-source include, and `CopyToUnity` target.

## 🚀 Build

From the `SourceGenerators` directory:

```bash
dotnet build Atomic.SourceGenerators.sln -c Release
```

By default, DLLs are **not** copied to the Unity project. To deploy them, provide the plugin folder explicitly:

```bash
dotnet build Atomic.SourceGenerators.sln -c Release \
  -p:AtomicDeployToUnity=true \
  -p:AtomicUnityPluginDir="C:\YourProject\Assets\Plugins\Atomic\SourceGenerators"
```

Only `.dll` files are copied (PDBs are not deployed).

## 📚 Generator-Specific Documentation

- [EntityAPIGenerator README](EntityAPIGenerator/README.md) — usage of `[EntityAPI]`, generated tag/value extension methods, and Unity import settings.
- [EntityAPIAnalyzer README](EntityAPIAnalyzer/README.md) — analyzer rules and code fixes for `[EntityAPI]` class declarations.
- [EventAPIGenerator README](EventAPIGenerator/README.md) — usage of `[EventAPI]`, generated event-bus extension methods, and Unity import settings.
- [EventAPIAnalyzer README](EventAPIAnalyzer/README.md) — analyzer rules and code fixes for `[EventAPI]` class declarations.

## ⚠️ Important Notes

- Keep marker attributes (`[EntityAPI]`, future `[EventAPI]`, etc.) in a **separate Unity assembly definition** (`Atomic.Entities`) so the generator can discover them during compilation.
- Do not use `ForAttributeWithMetadataName` — it requires Roslyn ≥ 4.6.0; Unity 6000 ships Roslyn 4.3.0. Use `SyntaxProvider.CreateSyntaxProvider` + manual attribute lookup instead.
- If a generator silently fails, check Unity's `Editor.log` for `CS9057` (Roslyn version mismatch) or missing DLLs in `Assets/Plugins/Atomic/SourceGenerators/`.
