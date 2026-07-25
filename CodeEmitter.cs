using EntityAPIGenerator.Models;

namespace EntityAPIGenerator
{
    /// <summary>
    /// Produces C# source code from <see cref="EntityAPIDefinition"/> models.
    /// Output matches the existing <c>.atomic</c> code generator's format.
    /// </summary>
    internal static class CodeEmitter
    {
        const string AggressiveInliningAttr = "[MethodImpl(MethodImplOptions.AggressiveInlining)]";

        /// <summary>
        /// Generates the full C# source for a given <see cref="EntityAPIDefinition"/>.
        /// </summary>
        public static string Emit(EntityAPIDefinition def)
        {
            var w = new CodeWriter();

            // Header
            w.Line("/**");
            w.Line(" * Code generation. Don't modify!");
            w.Line(" **/");
            w.Line();

            // Usings
            w.Line("using Atomic.Entities;");
            w.Line("using static Atomic.Entities.EntityKeyStore;");
            if (def.AggressiveInlining)
                w.Line("using System.Runtime.CompilerServices;");
            w.Line("using UnityEngine;");
            w.Line("using Atomic.Elements;");
            w.Line("#if UNITY_EDITOR");
            w.Line("using UnityEditor;");
            w.Line("#endif");

            // Namespace
            bool hasNamespace = !string.IsNullOrEmpty(def.Namespace);
            if (hasNamespace)
            {
                w.Line();
                w.Line($"namespace {def.Namespace}");
                w.Open();
            }

            // Class declaration
            w.Line("#if UNITY_EDITOR");
            w.Line("[InitializeOnLoad]");
            w.Line("#endif");
            w.Line($"public static partial class {def.ClassName}");
            w.Open();

            // Static int keys — values
            if (def.Values.Count > 0)
            {
                w.Line("///Values");
                foreach (var value in def.Values)
                    w.Line($"private static readonly int {value.Name}Key; // {value.ValueTypeName}");
            }

            // Static int keys — tags
            if (def.Tags.Count > 0)
            {
                if (def.Values.Count > 0) w.Line();
                w.Line("///Tags");
                foreach (var tag in def.Tags)
                    w.Line($"private static readonly int {tag.Name}Key;");
            }

            // Static constructor
            w.Line();
            w.Line($"static {def.ClassName}()");
            w.Open();
            foreach (var value in def.Values)
                w.Line($"{value.Name}Key = NameToId(nameof({value.Name}));");
            foreach (var tag in def.Tags)
                w.Line($"{tag.Name}Key = NameToId(nameof({tag.Name}));");
            w.Close();

            w.Line();

            // Extension methods — values
            if (def.Values.Count > 0)
            {
                w.Line("///Value Extensions");
                foreach (var value in def.Values)
                    EmitValueRegion(ref w, def.EntityTypeName, value, def.AggressiveInlining);
            }

            // Extension methods — tags
            if (def.Tags.Count > 0)
            {
                if (def.Values.Count > 0) w.Line();
                w.Line("///Tag Extensions");
                foreach (var tag in def.Tags)
                    EmitTagRegion(ref w, def.EntityTypeName, tag, def.AggressiveInlining);
            }

            w.Close(); // class
            if (hasNamespace) w.Close(); // namespace

            return w.Result;
        }

        static void EmitTagRegion(ref CodeWriter w, string entityTypeName, TagField tag, bool useInlining)
        {
            string key = tag.Name + "Key";

            w.Line();
            w.Line($"#region {tag.Name}");
            w.Line();

            // Has{Name}Tag
            if (useInlining) w.Line(AggressiveInliningAttr);
            w.Line($"public static bool Has{tag.Name}Tag(this {entityTypeName} entity) => entity.HasTag({key});");
            w.Line();

            // Add{Name}Tag
            if (useInlining) w.Line(AggressiveInliningAttr);
            w.Line($"public static bool Add{tag.Name}Tag(this {entityTypeName} entity) => entity.AddTag({key});");
            w.Line();

            // Del{Name}Tag
            if (useInlining) w.Line(AggressiveInliningAttr);
            w.Line($"public static bool Del{tag.Name}Tag(this {entityTypeName} entity) => entity.DelTag({key});");
            w.Line();

            w.Line("#endregion");
        }

        static void EmitValueRegion(ref CodeWriter w, string entityTypeName, ValueField value, bool useInlining)
        {
            bool isUnsafe = value.IsUnsafe;
            string getValueMethod = isUnsafe ? "GetValueUnsafe" : "GetValue";
            string tryGetMethod = isUnsafe ? "TryGetValueUnsafe" : "TryGetValue";
            string valueType = value.ValueTypeName;
            string key = value.Name + "Key";

            w.Line();
            w.Line($"#region {value.Name}");
            w.Line();

            // Get{Name}
            if (useInlining) w.Line(AggressiveInliningAttr);
            w.Line($"public static {valueType} Get{value.Name}(this {entityTypeName} entity) => entity.{getValueMethod}<{valueType}>({key});");
            w.Line();

            // Ref{Name} (only for unsafe)
            if (isUnsafe)
            {
                if (useInlining) w.Line(AggressiveInliningAttr);
                w.Line($"public static ref {valueType} Ref{value.Name}(this {entityTypeName} entity) => ref entity.{getValueMethod}<{valueType}>({key});");
                w.Line();
            }

            // TryGet{Name}
            if (useInlining) w.Line(AggressiveInliningAttr);
            w.Line($"public static bool TryGet{value.Name}(this {entityTypeName} entity, out {valueType} value) => entity.{tryGetMethod}({key}, out value);");
            w.Line();

            // Add{Name}
            if (useInlining) w.Line(AggressiveInliningAttr);
            w.Line($"public static void Add{value.Name}(this {entityTypeName} entity, {valueType} value) => entity.AddValue({key}, value);");
            w.Line();

            // Has{Name}
            if (useInlining) w.Line(AggressiveInliningAttr);
            w.Line($"public static bool Has{value.Name}(this {entityTypeName} entity) => entity.HasValue({key});");
            w.Line();

            // Del{Name}
            if (useInlining) w.Line(AggressiveInliningAttr);
            w.Line($"public static bool Del{value.Name}(this {entityTypeName} entity) => entity.DelValue({key});");
            w.Line();

            // Set{Name}
            if (useInlining) w.Line(AggressiveInliningAttr);
            w.Line($"public static void Set{value.Name}(this {entityTypeName} entity, {valueType} value) => entity.SetValue({key}, value);");
            w.Line();

            w.Line("#endregion");
        }
    }
}
