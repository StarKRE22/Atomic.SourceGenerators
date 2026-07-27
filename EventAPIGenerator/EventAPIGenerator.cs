using System;
using System.Linq;
using System.Reflection;
using Atomic.SourceGenerators.Shared;
using Microsoft.CodeAnalysis;

namespace EventAPIGenerator
{
    /// <summary>
    /// Incremental source generator that reads <c>[EventAPI]</c>-marked classes
    /// and generates extension methods for Atomic event keys.
    /// </summary>
    [Generator]
    public sealed class EventAPIGenerator : IIncrementalGenerator
    {
        public const string Id = "EventAPIGenerator";

        /// <summary>
        /// Name of the assembly that defines the <c>[EventAPI]</c> attribute.
        /// </summary>
        internal static readonly string CodegenAssemblyName = "Atomic.Entities";

        /// <summary>
        /// <c>true</c> when running as part of a compiler invocation (not IDE analysis).
        /// </summary>
        internal static readonly bool IsBuildTime = Assembly.GetEntryAssembly() != null;

        /// <inheritdoc/>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // TODO: implement EventAPI parsing and emission (mirror EntityAPIGenerator pattern).
            // This skeleton is intentionally empty so the solution structure and multi-DLL build
            // pipeline can be validated before the generator logic is added.

            var parseOptions = context.CompilationProvider.Select((compilation, _) => compilation.SyntaxTrees.FirstOrDefault()?.Options);

            context.RegisterSourceOutput(parseOptions, (sourceProductionContext, options) =>
            {
                if (options == null)
                    return;

                SourceOutputHelpers.Setup(options);
                SourceOutputHelpers.LogInfo($"[{Id}] Initialized. Event API generation is not yet implemented.");
            });
        }
    }
}
