---
name: unity-roslyn-generators
description: |
  Create Roslyn source generators, analyzers, and code-fix providers for Unity projects.
  Covers project setup, Roslyn API selection (CreateSyntaxProvider vs ForAttributeWithMetadataName),
  .csproj configuration for Unity 6000 LTS compatibility, analyzer/code-fix deployment,
  asmdef isolation for attribute assemblies, performance optimizations, and troubleshooting
  common pitfalls such as CS9057, CS1061, and analyzer load failures.
  Framework-agnostic — works with any Unity project structure.
metadata:
  model: opus
---

# Unity Roslyn Source Generators, Analyzers & Code Fixes

Project-agnostic guide for building and deploying Roslyn components that work inside Unity 6000.x (bundled Roslyn 4.3.0).

## When to use this skill

Use this guide when you need to:
- Create a C# source generator that runs inside Unity's compiler.
- Add a Roslyn analyzer to enforce project-specific rules.
- Add a code-fix provider that offers quick-fixes in the IDE.
- Debug why a generator/analyzer is not firing or producing wrong output.
- Choose safe deployment options for a team with varying project layouts.

## Do not use this skill when

- Writing runtime C# scripts or gameplay code in Unity.
- Working with source generators/analyzers outside Unity (ASP.NET, console apps, etc.).
- Setting up post-processing or asset import pipelines.
- Using a pre-existing code generator that already handles your use case.

## Core constraints

Unity 6000.3.x ships Roslyn 4.3.0. That imposes hard limits:

| Constraint | Value | Why it matters |
|------------|-------|----------------|
| `TargetFramework` | `netstandard2.0` | Unity compiler loads `netstandard2.0` analyzers/generators. |
| `LangVersion` | ≤ 12 recommended | C# 12 is broadly supported; newer features may fail in Unity. |
| `Microsoft.CodeAnalysis.CSharp` | **4.3.0** | Must match Unity's bundled Roslyn. Higher versions cause `CS9057`. |
| `Microsoft.CodeAnalysis.Analyzers` | 3.3.4 | Compatible analyzer helper package. |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | 4.3.0 only for code fixes | Required for `CodeFixProvider`; must **ExcludeAssets="runtime"**. |
| `IsRoslynComponent` | `true` | Marks the project as a Roslyn component. |
| `EnforceExtendedAnalyzerRules` | `true` | Required by modern analyzers; suppress `RS1035` if you do file IO. |

### Unity / Roslyn version mapping

| Unity version | Roslyn version | `Microsoft.CodeAnalysis.CSharp` | `ForAttributeWithMetadataName` |
|---------------|----------------|----------------------------------|--------------------------------|
| 2021.3 LTS    | ~3.11          | 3.11                             | ❌ |
| 2022.3 LTS    | ~4.0           | 4.0                              | ❌ |
| 6000.3 LTS    | **4.3.0**      | **4.3.0**                        | ❌ (requires ≥ 4.6) |
| 6000.x future | ≥ 4.6?         | ≥ 4.6.0                          | ✅ (if Roslyn updated) |

To verify your Unity version's Roslyn: open `Editor.log` and search for `csc.dll` version or `CS9057`.

## Project structure

A typical solution contains three kinds of projects:

```
SourceGenerators/
├── Directory.Build.props          # shared versions / settings
├── MyGenerator/                   # IIncrementalGenerator
│   ├── MyGenerator.csproj
│   ├── MyGenerator.cs             # generator entry point
│   ├── MyParser.cs                # syntax + semantic extraction
│   ├── MyCodeEmitter.cs           # source generation
│   └── Models/
│       └── MyDefinition.cs        # immutable cacheable model
├── MyAnalyzer/                    # DiagnosticAnalyzer
│   ├── MyAnalyzer.csproj
│   ├── MyAnalyzer.cs              # analyzer logic
│   ├── MyDiagnostics.cs           # descriptors
│   └── MyCodeFixProvider.cs       # optional code fixes
└── MyAttributeAssembly/           # Unity runtime assembly
    └── MyAttribute.cs             # marker attribute
```

**Rule:** The marker attribute must live in a **separate Unity assembly** (asmdef), not in the generator assembly. The generator discovers the attribute by name/namespace via the semantic model; the Unity runtime needs the attribute to compile user code.

## Shared MSBuild props

Create `Directory.Build.props` next to the solution:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.3.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
  </ItemGroup>

  <!-- Optional: suppress "do not use file IO in source generators" (RS1035).
       Only if you write debug output behind a custom define. -->
  <PropertyGroup>
    <NoWarn>$(NoWarn);RS1035</NoWarn>
  </PropertyGroup>
</Project>
```

## Generator project (.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MyGenerator</AssemblyName>
    <RootNamespace>MyGenerator</RootNamespace>
  </PropertyGroup>

  <!-- Optional shared helpers (compiled into each generator DLL) -->
  <ItemGroup>
    <Compile Include="..\SharedSource\*.cs" Link="Shared\%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

Do **not** reference a shared class-library project from the generator. Unity's analyzer loader may not resolve a second DLL. Compile shared sources directly into each generator.

## Generator implementation pattern

### 1. Entry point

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;

namespace MyGenerator
{
    [Generator]
    public sealed class MyGenerator : IIncrementalGenerator
    {
        public const string Id = "MyGenerator";
        internal static readonly string CodegenAssemblyName = "MyRuntimeAssembly";

        // Optional: report diagnostics so developers can confirm the generator ran.
        internal static readonly DiagnosticDescriptor TraceInfo = new DiagnosticDescriptor(
            id: "MYGEN0001",
            title: "Generator trace",
            messageFormat: "{0}",
            category: "MyGenerator",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var pipeline = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => MyParser.IsCandidate(node),
                transform: static (ctx, _) => MyParser.Transform(ctx)
            );

            var definitions = pipeline
                .Where(static def => def.HasValue)
                .Select(static (def, _) => def!.Value);

            var combined = definitions.Collect()
                .Combine(context.CompilationProvider)
                .Combine(context.ParseOptionsProvider);

            context.RegisterSourceOutput(combined, (spc, tuple) =>
            {
                var ((defs, compilation), parseOptions) = tuple;

                if (!ShouldRun(compilation))
                    return;

                foreach (var def in defs)
                {
                    try
                    {
                        string source = MyCodeEmitter.Emit(def);
                        string hintName = $"{def.ClassName}.g.cs";
                        spc.AddSource(hintName, source);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            MyDiagnostics.InternalError,
                            Location.None,
                            ex.Message));
                    }

                    // Optional: emit an info diagnostic so you can see the generator ran.
                    spc.ReportDiagnostic(Diagnostic.Create(
                        TraceInfo,
                        Location.None,
                        $"Generated {hintName}"));
                }
            });
        }

        internal static bool ShouldRun(Compilation compilation)
        {
            // Skip the assembly that defines the marker attribute.
            if (compilation.Assembly.Name == CodegenAssemblyName)
                return false;

            // Only run if the compilation references the runtime assembly.
            return compilation.ReferencedAssemblyNames.Any(n => n.Name == CodegenAssemblyName);
        }
    }
}
```

### 2. Candidate predicate (syntax-only, cheap)

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyGenerator
{
    internal static class MyParser
    {
        public static bool IsCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDecl &&
                   classDecl.AttributeLists
                            .SelectMany(al => al.Attributes)
                            .Any(attr => IsAttributeName(attr, "MyAttribute"));
        }

        private static bool IsAttributeName(AttributeSyntax attr, string name)
        {
            string? attrName = attr.Name switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                QualifiedNameSyntax q   => q.Right.Identifier.Text,
                _ => null
            };

            return attrName == name || attrName == name + "Attribute";
        }
    }
}
```

### 3. Semantic transform (extract model)

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyGenerator
{
    internal static class MyParser
    {
        public static MyDefinition? Transform(GeneratorSyntaxContext context)
        {
            if (context.Node is not ClassDeclarationSyntax classDecl)
                return null;

            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol == null)
                return null;

            if (!HasAttribute(classSymbol, "MyAttribute"))
                return null;

            string ns = GetNamespace(classDecl);
            var fields = new List<MyField>();

            foreach (var member in classDecl.Members.OfType<FieldDeclarationSyntax>())
            {
                if (!member.Modifiers.Any(SyntaxKind.StaticKeyword))
                    continue;

                if (member.Declaration.Variables.Count != 1)
                    continue;

                var variable = member.Declaration.Variables[0];
                var fieldSymbol = context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol?.Type is not INamedTypeSymbol namedType)
                    continue;

                if (namedType.ContainingNamespace?.ToDisplayString() != "MyRuntimeNamespace" ||
                    namedType.Name != "MyKeyType")
                    continue;

                fields.Add(new MyField(variable.Identifier.Text, namedType.TypeArguments.Select(t => t.ToDisplayString()).ToList()));
            }

            // Extract attribute arguments if needed
            if (TryGetAttribute(classSymbol, "MyAttribute", out var attr))
            {
                // typeof(T) constructor argument
                if (attr.ConstructorArguments.Length > 0 &&
                    attr.ConstructorArguments[0].Kind == TypedConstantKind.Type &&
                    attr.ConstructorArguments[0].Value is ITypeSymbol typeArg)
                {
                    string typeName = typeArg.ToDisplayString();
                    // use typeName in the model
                }

                // Named argument with default fallback
                bool someFlag = HasNamedArg(attr, "SomeFlag")
                    ? TryGetNamedArgBool(attr, "SomeFlag")
                    : true; // match the C# default in the attribute definition
            }

            return new MyDefinition(ns, classDecl.Identifier.Text, fields.AsReadOnly());
        }

        private static bool HasAttribute(INamedTypeSymbol symbol, string name)
        {
            return symbol.GetAttributes().Any(a =>
                a.AttributeClass?.Name == name ||
                a.AttributeClass?.Name == name + "Attribute");
        }

        private static bool TryGetAttribute(INamedTypeSymbol symbol, string name, out AttributeData attribute)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.Name == name ||
                    attr.AttributeClass?.Name == name + "Attribute")
                {
                    attribute = attr;
                    return true;
                }
            }
            attribute = null!;
            return false;
        }

        private static bool HasNamedArg(AttributeData attr, string name)
        {
            foreach (var kvp in attr.NamedArguments)
                if (kvp.Key == name) return true;
            return false;
        }

        private static bool TryGetNamedArgBool(AttributeData attr, string name)
        {
            foreach (var kvp in attr.NamedArguments)
            {
                if (kvp.Key == name &&
                    kvp.Value.Kind == TypedConstantKind.Primitive &&
                    kvp.Value.Value is bool value)
                    return value;
            }
            return false;
        }

        private static string GetNamespace(SyntaxNode node)
        {
            for (var current = node.Parent; current != null; current = current.Parent)
            {
                if (current is BaseNamespaceDeclarationSyntax ns)
                    return ns.Name.ToString();
            }
            return string.Empty;
        }
    }
}
```

**Important:** `NamedArguments` only contains values **explicitly set** at the call site. If the attribute property has a non-false default, you must check presence and fall back to the C# default value.

### 4. Immutable model (for incremental caching)

```csharp
using System;
using System.Collections.Generic;

namespace MyGenerator
{
    public readonly struct MyDefinition : IEquatable<MyDefinition>
    {
        public string Namespace { get; }
        public string ClassName { get; }
        public IReadOnlyList<MyField> Fields { get; }

        public MyDefinition(string ns, string className, IReadOnlyList<MyField> fields)
        {
            Namespace = ns;
            ClassName = className;
            Fields = fields;
        }

        public bool Equals(MyDefinition other) =>
            Namespace == other.Namespace &&
            ClassName == other.ClassName &&
            SequenceEqual(Fields, other.Fields);

        public override bool Equals(object? obj) => obj is MyDefinition other && Equals(other);

        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 31 + (Namespace?.GetHashCode() ?? 0);
            hash = hash * 31 + (ClassName?.GetHashCode() ?? 0);
            foreach (var f in Fields) hash = hash * 31 + f.GetHashCode();
            return hash;
        }

        private static bool SequenceEqual(IReadOnlyList<MyField> a, IReadOnlyList<MyField> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }
    }

    public readonly struct MyField : IEquatable<MyField>
    {
        public string Name { get; }
        public IReadOnlyList<string> TypeArgs { get; }

        public MyField(string name, IReadOnlyList<string> typeArgs)
        {
            Name = name;
            TypeArgs = typeArgs;
        }

        public bool Equals(MyField other) =>
            Name == other.Name &&
            SequenceEqual(TypeArgs, other.TypeArgs);

        public override bool Equals(object? obj) => obj is MyField other && Equals(other);
        public override int GetHashCode() => Name.GetHashCode();

        private static bool SequenceEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
```

### 5. Code emitter

```csharp
using System.Text;

namespace MyGenerator
{
    internal static class MyCodeEmitter
    {
        public static string Emit(MyDefinition def)
        {
            var sb = new StringBuilder();
            sb.AppendLine("/**");
            sb.AppendLine(" * Code generation. Don't modify!");
            sb.AppendLine(" **/");
            sb.AppendLine();
            sb.AppendLine("namespace MyRuntimeNamespace;");
            sb.AppendLine();
            sb.AppendLine($"public static partial class {def.ClassName}");
            sb.AppendLine("{");

            foreach (var field in def.Fields)
            {
                sb.AppendLine($"    public static void Invoke{field.Name}() =>");
                sb.AppendLine($"        MyRuntimeType.DoSomething({def.ClassName}.{field.Name}.Id);");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
```

Use `StringBuilder` or a small `CodeWriter` helper. Avoid hand-rolling indentation without helpers.

## Analyzer + code fix

### Analyzer project (.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MyAnalyzer</AssemblyName>
    <RootNamespace>MyAnalyzer</RootNamespace>
    <NoWarn>$(NoWarn);RS2007;RS2008</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces"
                      Version="4.3.0"
                      PrivateAssets="all"
                      ExcludeAssets="runtime" />
  </ItemGroup>
</Project>
```

`RS2007/RS2008` suppress analyzer-release tracking. If you do ship releases, create `AnalyzerReleases.Shipped.md` / `.Unshipped.md` instead.

### Diagnostic descriptors

```csharp
using Microsoft.CodeAnalysis;

namespace MyAnalyzer
{
    internal static class MyDiagnostics
    {
        public static readonly DiagnosticDescriptor MissingInitializer = new DiagnosticDescriptor(
            id: "MY0001",
            title: "Key field must be initialized",
            messageFormat: "Key field '{0}' must be initialized with a non-default value.",
            category: "MyRules",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
```

### Analyzer

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MyAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MyAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(MyDiagnostics.MissingInitializer);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
        }

        private static void AnalyzeField(SyntaxNodeAnalysisContext context)
        {
            var fieldDecl = (FieldDeclarationSyntax)context.Node;
            if (!fieldDecl.Modifiers.Any(SyntaxKind.StaticKeyword))
                return;

            if (fieldDecl.Parent is not ClassDeclarationSyntax classDecl)
                return;

            if (!HasAttribute(classDecl, "MyAttribute"))
                return;

            foreach (var variable in fieldDecl.Declaration.Variables)
            {
                if (!IsTargetKey(context.SemanticModel, variable, context.CancellationToken))
                    continue;

                if (variable.Initializer == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MyDiagnostics.MissingInitializer,
                        variable.Identifier.GetLocation(),
                        variable.Identifier.Text));
                }
            }
        }

        private static bool HasAttribute(ClassDeclarationSyntax classDecl, string name)
        {
            return classDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr => attr.Name is IdentifierNameSyntax id &&
                             (id.Identifier.Text == name || id.Identifier.Text == name + "Attribute"));
        }

        private static bool IsTargetKey(SemanticModel model, VariableDeclaratorSyntax variable, CancellationToken ct)
        {
            var symbol = model.GetDeclaredSymbol(variable, ct) as IFieldSymbol;
            if (symbol?.Type is not INamedTypeSymbol namedType)
                return false;

            return namedType.ContainingNamespace?.ToDisplayString() == "MyRuntimeNamespace" &&
                   namedType.Name == "MyKeyType";
        }
    }
}
```

### Code fix provider

```csharp
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyAnalyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MyCodeFixProvider)), Shared]
    public sealed class MyCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(MyDiagnostics.MissingInitializer.Id);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            if (root == null) return;

            foreach (var diagnostic in context.Diagnostics)
            {
                var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                var variable = node as VariableDeclaratorSyntax ?? node.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
                if (variable == null) continue;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Initialize '{variable.Identifier.Text}'",
                        createChangedDocument: c => AddInitializer(context.Document, variable, c),
                        equivalenceKey: nameof(MyCodeFixProvider)),
                    diagnostic);
            }
        }

        private static async Task<Document> AddInitializer(Document document, VariableDeclaratorSyntax variable, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            if (root == null) return document;

            var initializer = SyntaxFactory.EqualsValueClause(
                SyntaxFactory.Token(SyntaxKind.EqualsToken).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.ImplicitObjectCreationExpression(
                    SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(
                                SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.IdentifierName("nameof"),
                                    SyntaxFactory.ArgumentList(
                                        SyntaxFactory.SingletonSeparatedList(
                                            SyntaxFactory.Argument(
                                                SyntaxFactory.IdentifierName(variable.Identifier.Text)))))))),
                    default))
                .WithLeadingTrivia(SyntaxFactory.Space);

            var newVariable = variable.WithInitializer(initializer);
            return document.WithSyntaxRoot(root.ReplaceNode(variable, newVariable));
        }
    }
}
```

## Deployment options

### Option A: Manual copy (safest, recommended for varied team layouts)

1. Build the solution: `dotnet build MyGenerators.sln -c Release`
2. Copy `bin/Release/netstandard2.0/*.dll` (no PDBs) to:
   ```
   Assets/Plugins/MyGenerators/
   ```
3. In Unity, select each DLL and add the **Asset Label** `RoslynAnalyzer`.
4. Under **Select platforms for plugin**, uncheck **Any Platform**.
5. Under **Include Platforms**, uncheck **Editor** and **Standalone** (and any other platforms).

Leaving all platforms unchecked is correct; the DLL is only a compile-time analyzer/generator.

### Option B: Conditional MSBuild copy

Add to each `.csproj`:

```xml
<!-- Optional: copy DLL to a Unity plugin folder.
     Disabled by default. Use:
       dotnet build ... -p:MyDeployToUnity=true -p:MyUnityPluginDir=C:\Project\Assets\Plugins\MyGenerators
-->
<Target Name="CopyToUnity" AfterTargets="Build"
        Condition="'$(MyDeployToUnity)' == 'true' And '$(MyUnityPluginDir)' != ''">
  <Copy SourceFiles="$(TargetDir)$(TargetName).dll"
        DestinationFolder="$([MSBuild]::NormalizeDirectory('$(MyUnityPluginDir)'))"
        SkipUnchangedFiles="true" />
  <Message Text="$(TargetName) deployed to $(MyUnityPluginDir)" Importance="high" />
</Target>
```

**Why no default path?** Relative paths break when project folders contain spaces or non-ASCII characters, and when users place the Unity project at different absolute paths.

**Why no PDB?** PDBs are not needed by the Unity compiler and can confuse the analyzer loader.

### Option C: CI / post-build script

Use a shell script or GitHub Action that builds the solution and copies the DLLs to a configured Unity plugin folder. This keeps the `.csproj` files clean.

## Unity import settings

After copying a DLL into `Assets/Plugins/...`:

1. Select the DLL in the Project window.
2. In the Inspector:
   - Add Asset Label: `RoslynAnalyzer`.
   - Under **Select platforms for plugin**, uncheck **Any Platform**.
   - Under **Include Platforms**, uncheck **Editor** and **Standalone** (and any other platforms).
3. If the DLL doesn't appear under `Assets/Plugins`, check Unity's `Editor.log` for load errors.

> **Note:** Unity does **not** have an "Analyze Sources" or "Process Sources" checkbox. The `RoslynAnalyzer` asset label and platform settings are what activate analyzers and source generators.

## Performance considerations

Source generators and analyzers run on every compilation (and often on every keystroke in the IDE). Use these patterns to keep them fast:

### 1. Use immutable, comparable models

Incremental generators cache pipeline stages by equality. If your model is a class with default reference equality, Roslyn will re-run downstream steps every time.

```csharp
public readonly struct MyDefinition : IEquatable<MyDefinition>
{
    public string Namespace { get; }
    public string ClassName { get; }
    public IReadOnlyList<MyField> Fields { get; }

    // Implement Equals + GetHashCode carefully.
    // Only store primitive data: strings, ints, bools.
    // Do NOT store SyntaxNode, ISymbol, SemanticModel, or Location.
}
```

### 2. Keep the syntax predicate cheap

`CreateSyntaxProvider` runs the predicate on every syntax node. It must not allocate or use the semantic model.

```csharp
public static bool IsCandidate(SyntaxNode node)
{
    // Fast: no allocations, no semantic model.
    return node is ClassDeclarationSyntax classDecl &&
           classDecl.AttributeLists.Count > 0;
}
```

Do the expensive attribute-name resolution and semantic checks inside `Transform`, which runs only on candidates.

### 3. Bail out early

Skip compilations that cannot contain your targets:

```csharp
internal static bool ShouldRun(Compilation compilation)
{
    // The assembly that defines the marker attribute never uses it.
    if (compilation.Assembly.Name == RuntimeAssemblyName)
        return false;

    // Only run if the user project references the runtime assembly.
    return compilation.ReferencedAssemblyNames.Any(n => n.Name == RuntimeAssemblyName);
}
```

### 4. Avoid LINQ in the transform

LINQ allocates enumerators. In hot generator paths, prefer `foreach`:

```csharp
foreach (var member in classDecl.Members)
{
    if (member is not FieldDeclarationSyntax fieldDecl)
        continue;

    // process field
}
```

### 5. Reuse StringBuilder / CodeWriter

If the emitter runs for many classes, allocate the builder once per emit and clear it, or pool it:

```csharp
internal static class CodeEmitter
{
    public static string Emit(MyDefinition def)
    {
        var w = new CodeWriter(); // or StringBuilder
        // ... append lines
        return w.Result;
    }
}
```

### 6. Skip design-time builds when possible

Generators can run in the IDE on every keystroke. If generation is expensive, skip design-time builds:

```csharp
internal static readonly bool IsBuildTime = Assembly.GetEntryAssembly() != null;

if (!IsBuildTime)
    return;
```

Note: generated code will not appear in the IDE until a real build or domain reload.

### 7. Analyzer concurrency

Always enable concurrent execution and configure generated-code analysis:

```csharp
public override void Initialize(AnalysisContext context)
{
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();
    context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.FieldDeclaration);
}
```

## Common gotchas

### Do not use `ForAttributeWithMetadataName` or `GeneratorAttributeSyntaxContext`

```csharp
// ❌ Requires Roslyn >= 4.6.0 — not available in Unity 6000.
context.SyntaxProvider.ForAttributeWithMetadataName(...)

// ❌ GeneratorAttributeSyntaxContext is also not available in Roslyn 4.3.0.
// Use GeneratorSyntaxContext with manual attribute and symbol lookup.

// ✅ Use CreateSyntaxProvider + manual semantic attribute lookup.
context.SyntaxProvider.CreateSyntaxProvider(predicate, transform)
```

Inside `Transform`, manually resolve the class symbol and call `GetAttributes()` to find the marker attribute.

### Do not reference Workspaces in the analyzer runtime

```xml
<!-- ✅ Correct: reference only for compile, exclude runtime assets. -->
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces"
                  Version="4.3.0"
                  PrivateAssets="all"
                  ExcludeAssets="runtime" />
```

### Do not bundle shared helpers as a separate DLL

Compile shared source files directly into each generator/analyzer project:

```xml
<Compile Include="..\Shared\*.cs" Link="Shared\%(Filename)%(Extension)" />
```

### Generated code must be in `partial` classes

The user's class must be declared `partial` so the generator can add a matching partial declaration.

```csharp
[MyApi]
public static partial class MyApi { ... }
```

### Namespace detection

Use `BaseNamespaceDeclarationSyntax` (covers regular and file-scoped namespaces):

```csharp
private static string GetNamespace(SyntaxNode node)
{
    for (var current = node.Parent; current != null; current = current.Parent)
    {
        if (current is BaseNamespaceDeclarationSyntax ns)
            return ns.Name.ToString();
    }
    return string.Empty;
}
```

### Avoid file IO in generators unless guarded

If you write debug output, guard it behind a custom MSBuild define or environment variable and suppress `RS1035`.

```csharp
if (context.AnalyzerConfigOptions.GlobalOptions.TryGetValue(
    "build_property.MyGeneratorOutputPath", out var path))
{
    File.WriteAllText(Path.Combine(path, hintName), source);
}
```

### Analyzer release tracking

If you don't ship analyzer releases, suppress `RS2007` and `RS2008`:

```xml
<NoWarn>$(NoWarn);RS2007;RS2008</NoWarn>
```

If you do ship releases, add `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md`.

### IDE vs build-time

Source generators may run in the IDE on every keystroke. To reduce overhead, skip design-time builds if needed:

```csharp
internal static readonly bool IsBuildTime = Assembly.GetEntryAssembly() != null;

if (!IsBuildTime) return;
```

Be aware that generated code will only appear in the IDE after a real build/domain reload.

### Check Unity logs

If a generator/analyzer silently fails:

- Open `Editor.log` (`%LOCALAPPDATA%\Unity\Editor\Editor.log` on Windows).
- Search for `CS9057` (Roslyn version mismatch) or the generator/analyzer name.
- Verify the DLL is labeled `RoslynAnalyzer`.
- Verify the DLL is in a folder Unity scans (under `Assets/`).

## Troubleshooting

### #1 silent failure: `CS9057` (Roslyn version mismatch)

If the generator or analyzer assembly was compiled against a newer `Microsoft.CodeAnalysis.CSharp` than Unity ships, it is silently ignored and no generated code is produced.

**Check:** Open `Editor.log` (`%LOCALAPPDATA%\Unity\Editor\Editor.log` on Windows) and search for `CS9057`.

**Fix:** Pin `Microsoft.CodeAnalysis.CSharp` to the version matching Unity's bundled Roslyn:

| Unity version | Roslyn version | Package version |
|---------------|----------------|-----------------|
| 6000.3 LTS    | **4.3.0**      | **4.3.0**       |

Also replace `ForAttributeWithMetadataName` with `CreateSyntaxProvider` + manual attribute lookup.

### Generator doesn't run

| Symptom | Cause | Fix |
|---------|-------|-----|
| No diagnostic or generated code | `CS9057` Roslyn version mismatch | Downgrade package to Unity's Roslyn version |
| No warning, no output | DLL not in plugin folder | Copy to `Assets/Plugins/...` or enable deploy property |
| `CS1061` on generated members | Generator didn't run | Check `Editor.log` for errors; add diagnostic logging |
| `CS1061` on generated members | Marker attribute not in separate asmdef | Move attribute to a standalone asmdef referenced by consumers |
| Works in IDE but not Unity | Unity uses bundled Roslyn | Match Roslyn version exactly |

### Analyzer / code fix not showing

| Symptom | Cause | Fix |
|---------|-------|-----|
| No squiggles | DLL not loaded | Verify DLL is in `Assets/Plugins/...`, label is `RoslynAnalyzer` |
| No quick fix | Workspaces bundled as runtime | Ensure `ExcludeAssets="runtime"` on Workspaces package |
| `RS2007`/`RS2008` build error | Missing analyzer release tracking | Add `NoWarn` or create `AnalyzerReleases.*.md` |

### .NET SDK build errors

| Error | Fix |
|-------|-----|
| `NETSDK1136` — invalid target framework | Use `netstandard2.0` (not `netstandard2.1`) |
| `CS1705` — assembly version mismatch | Pin `Microsoft.CodeAnalysis.CSharp` to Unity's Roslyn version |
| `RS1035` — file IO in analyzer/generator | Suppress or guard IO behind a config flag |

### Generated code errors

| Error | Cause | Fix |
|-------|-------|-----|
| `CS0246` — type not found | Missing `using` in generated code | Add the namespace of the target type |
| `CS0118` — field used as type | Wrong type string format | Use `ToDisplayString()` for full type names |
| `CS0103` — `nameof(X)` not found | Symbol not in generated scope | Qualify with class name or declare the symbol first |
| `CS0307` — type arg constraints violated | Generic constraints don't match | Verify `where` clauses on generated types |

## Response approach

When asked to create or debug a Unity Roslyn generator/analyzer/code fix:

1. **Check `Editor.log` for `CS9057`** — this is the #1 cause of silent generator failure.
2. **Verify the DLL location** — it must be under `Assets/Plugins/...` with the `RoslynAnalyzer` label.
3. **Confirm marker attributes are in a separate asmdef** if applicable.
4. **Check for generator diagnostics** — if the generator doesn't log, it likely didn't run.
5. **Build the generator solution from CLI** first to isolate .NET issues from Unity issues.
6. **Inspect generated code** — enable a debug output define or manually inspect the generated file for compilation errors.

## Example interactions

**Q:** "My source generator doesn't work in Unity. I see no generated code."

**A:** Check `Editor.log` for `CS9057`. If present, downgrade `Microsoft.CodeAnalysis.CSharp` to the version matching Unity's bundled Roslyn (e.g., 4.3.0 for Unity 6000) and replace `ForAttributeWithMetadataName` with `CreateSyntaxProvider` + manual attribute lookup.

**Q:** "The generated members are not found even though the generator runs."

**A:** Verify the marker attribute is in a separate asmdef. If the attribute is in the same assembly as the consuming code, move it to a standalone assembly that compiles first.

**Q:** "A named argument with a default value of `true` is being read as `false`."

**A:** `NamedArguments` only contains values explicitly set at the call site. Use `HasNamedArg()` to check presence, then fall back to the C# default value of the property.

**Q:** "Quick fixes don't appear in the IDE."

**A:** Ensure the analyzer project references `Microsoft.CodeAnalysis.CSharp.Workspaces` with `ExcludeAssets="runtime"`. Verify the DLL is in `Assets/Plugins/...`, has the `RoslynAnalyzer` asset label, and has no platforms selected in the plugin inspector.

## Minimal end-to-end checklist

- [ ] Marker attribute in a Unity runtime assembly.
- [ ] Generator project: `netstandard2.0`, `Microsoft.CodeAnalysis.CSharp` 4.3.0, `IsRoslynComponent=true`.
- [ ] Generator uses `CreateSyntaxProvider` for Roslyn 4.3 compatibility.
- [ ] Parser extracts an immutable `IEquatable` model.
- [ ] Emitter produces valid C# into a partial class.
- [ ] Analyzer project: same base packages, optionally `Workspaces` for code fixes.
- [ ] Analyzer reports diagnostics with unique IDs and categories.
- [ ] Code fix provider uses `ExportCodeFixProvider` and `WellKnownFixAllProviders.BatchFixer`.
- [ ] Build succeeds with 0 errors and 0 warnings.
- [ ] DLLs deployed to Unity with `RoslynAnalyzer` label and no platforms selected (compile-time only).
- [ ] Unity `Editor.log` shows no `CS9057` or load errors.

## References

- Roslyn docs: https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md
- Unity source generator docs: https://docs.unity3d.com/6000.0/Documentation/Manual/roslyn-analyzers.html
- Microsoft CodeAnalysis packages: https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp
