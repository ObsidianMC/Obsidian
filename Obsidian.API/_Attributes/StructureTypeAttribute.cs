namespace Obsidian.API;

/// <summary>
/// Marks the class that implements a vanilla structure type (e.g. <c>minecraft:jigsaw</c>) or a type used inside
/// structure data (placements, pool elements...), so the worldgen registries can be generated from vanilla's JSON.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class StructureTypeAttribute(string identifier) : Attribute
{
    public string Identifier { get; } = identifier;
}
