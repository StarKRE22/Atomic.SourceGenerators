using System;
using System.Collections.Generic;
using System.Linq;
using EntityAPIGenerator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityAPIGenerator
{
    /// <summary>
    /// Parses <c>[EntityAPI]</c>-annotated classes from Roslyn syntax + semantic models.
    /// </summary>
    internal static class EntityAPIParser
    {
        private const string EntityAPIAttributeName = "EntityAPI";
        private const string UnsafeAttributeName = "Unsafe";


        /// <summary>
        /// Quick syntax check — does this node look like a class with <c>[EntityAPI]</c>?
        /// Runs before the semantic model is available (cheap).
        /// </summary>
        public static bool IsCandidate(SyntaxNode node)
        {
            if (node is not ClassDeclarationSyntax classDecl)
                return false;

            return classDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr => IsEntityAPIAttributeName(attr));
        }

        /// <summary>
        /// Semantic transform — extracts <see cref="EntityAPIDefinition"/> from a candidate class.
        /// Returns <c>null</c> if the class doesn't actually have the attribute at the semantic level.
        /// </summary>
        public static EntityAPIDefinition? Transform(GeneratorSyntaxContext context)
        {
            if (context.Node is not ClassDeclarationSyntax classDecl)
                return null;

            SemanticModel semanticModel = context.SemanticModel;

            // Resolve class symbol and find [EntityAPI] attribute manually
            INamedTypeSymbol? classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol == null)
                return null;

            if (!TryGetEntityAPIAttribute(classSymbol.GetAttributes(), out var attributeData))
                return null;

            // Extract entity type from constructor argument: typeof(IPlayerContext)
            if (!TryExtractEntityType(attributeData!, out var entityTypeName) || entityTypeName == null)
                return null;

            // Extract class-level Unsafe flag
            bool classUnsafe = TryGetNamedArgBool(attributeData!, "Unsafe");

            // Extract class-level AggressiveInlining flag.
            // Attribute defaults to true, so we check if the key is present first.
            bool classAggressiveInlining = HasNamedArg(attributeData!, "AggressiveInlining")
                ? TryGetNamedArgBool(attributeData!, "AggressiveInlining")
                : true;

            // Get namespace
            string ns = GetNamespace(classDecl);

            // Parse fields
            var values = new List<ValueField>();
            var tags = new List<TagField>();

            foreach (var member in classDecl.Members)
            {
                if (member is not FieldDeclarationSyntax fieldDecl)
                    continue;

                // Only static fields
                if (!fieldDecl.Modifiers.Any(SyntaxKind.StaticKeyword))
                    continue;

                // Only fields with exactly one variable (normal pattern)
                if (fieldDecl.Declaration.Variables.Count != 1)
                    continue;

                var variable = fieldDecl.Declaration.Variables[0];
                string fieldName = variable.Identifier.Text;

                // Resolve field symbol
                var fieldSymbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol == null)
                    continue;

                // Tag / TagKey<T> → tag, ValueKey<T1,T2> → value (extract T2), anything else → value
                var namedType = fieldSymbol.Type as INamedTypeSymbol;
                bool isAtomicEntities = namedType != null &&
                    namedType.ContainingNamespace?.ToDisplayString() == "Atomic.Entities";

                if (isAtomicEntities &&
                    (namedType!.Name == "Tag" || namedType.Name == "TagKey"))
                {
                    tags.Add(new TagField(fieldName));
                }
                else
                {
                    bool fieldUnsafe = classUnsafe || HasUnsafeAttribute(fieldDecl);

                    // ValueKey<TContext, TValue> → use the second generic argument as value type
                    string valueTypeStr;
                    if (isAtomicEntities &&
                        namedType!.Name == "ValueKey" &&
                        namedType.TypeArguments.Length >= 2)
                    {
                        valueTypeStr = namedType.TypeArguments[1].ToDisplayString();
                    }
                    else
                    {
                        valueTypeStr = fieldSymbol.Type.ToDisplayString();
                    }

                    values.Add(new ValueField(fieldName, entityTypeName, valueTypeStr, fieldUnsafe));
                }
            }

            return new EntityAPIDefinition(
                ns: ns,
                className: classDecl.Identifier.Text,
                entityTypeName: entityTypeName,
                unsafeFlag: classUnsafe,
                aggressiveInlining: classAggressiveInlining,
                values: values.AsReadOnly(),
                tags: tags.AsReadOnly()
            );
        }

        private static bool IsEntityAPIAttributeName(AttributeSyntax attr)
        {
            string? name = attr.Name switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                QualifiedNameSyntax q => q.Right.Identifier.Text,
                _ => null
            };

            return name == EntityAPIAttributeName ||
                   name == EntityAPIAttributeName + "Attribute";
        }

        private static bool TryGetEntityAPIAttribute(
            System.Collections.Generic.IEnumerable<AttributeData> attributes,
            out AttributeData? result)
        {
            foreach (var attr in attributes)
            {
                if (attr.AttributeClass?.Name == EntityAPIAttributeName ||
                    attr.AttributeClass?.Name == EntityAPIAttributeName + "Attribute")
                {
                    result = attr;
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static bool TryExtractEntityType(AttributeData attribute, out string? entityTypeName)
        {
            entityTypeName = null;

            if (attribute.ConstructorArguments.Length == 0)
                return false;

            var arg = attribute.ConstructorArguments[0];

            // The constructor argument is a System.Type passed via typeof().
            // In the semantic model this resolves to a ITypeSymbol.
            if (arg.Kind != TypedConstantKind.Type)
                return false;

            if (arg.Value is not ITypeSymbol typeSymbol)
                return false;

            entityTypeName = typeSymbol.ToDisplayString();
            return true;
        }

        private static bool TryGetNamedArgBool(AttributeData attribute, string argName)
        {
            foreach (var kvp in attribute.NamedArguments)
            {
                if (kvp.Key == argName &&
                    kvp.Value.Kind == TypedConstantKind.Primitive &&
                    kvp.Value.Value is bool value)
                {
                    return value;
                }
            }
            return false;
        }

        private static bool HasNamedArg(AttributeData attribute, string argName)
        {
            foreach (var kvp in attribute.NamedArguments)
            {
                if (kvp.Key == argName)
                    return true;
            }
            return false;
        }



        private static bool HasUnsafeAttribute(FieldDeclarationSyntax fieldDecl)
        {
            return fieldDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr =>
                {
                string? name = attr.Name switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    QualifiedNameSyntax q => q.Right.Identifier.Text,
                    _ => null
                };
                return name == UnsafeAttributeName ||
                       name == UnsafeAttributeName + "Attribute";
                });
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
