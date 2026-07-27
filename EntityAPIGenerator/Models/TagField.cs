using System;

namespace EntityAPIGenerator.Models
{
    /// <summary>
    /// Represents a <c>Tag</c> static field parsed from an <c>[EntityAPI]</c> class,
    /// used to generate <c>HasXxxTag</c>, <c>AddXxxTag</c>, <c>DelXxxTag</c> extension methods.
    /// </summary>
    public readonly struct TagField : IEquatable<TagField>
    {
        /// <summary>Field name, e.g. <c>IsAlive</c>, <c>IsStunned</c>.</summary>
        public string Name { get; }

        public TagField(string name)
        {
            Name = name;
        }

        public bool Equals(TagField other) => Name == other.Name;

        public override bool Equals(object obj) =>
            obj is TagField other && Equals(other);

        public override int GetHashCode() => Name?.GetHashCode() ?? 0;
    }
}
