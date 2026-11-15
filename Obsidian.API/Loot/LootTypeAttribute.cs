namespace Obsidian.API.Loot;

/// <summary>
/// Marks the class that implements a vanilla loot type (an entry, function, condition or number provider), e.g.
/// <c>[LootType("minecraft:set_count")]</c>. The loot table source generator instantiates the class whose resource
/// location matches the JSON discriminator and that is assignable to the property being filled.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LootTypeAttribute(string resourceLocation) : Attribute
{
    /// <summary>
    /// Gets the loot type's id, e.g. <c>minecraft:set_count</c>, matched against the JSON discriminator.
    /// </summary>
    public string ResourceLocation { get; } = resourceLocation;
}
