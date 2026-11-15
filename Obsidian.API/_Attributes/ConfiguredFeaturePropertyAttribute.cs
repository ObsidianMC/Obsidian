namespace Obsidian.API;

/// <summary>
/// Marks a class as the implementation of a type used inside feature configurations, like the block state provider
/// <c>minecraft:noise_provider</c>, that data files name by their <c>type</c>.
/// </summary>
/// <param name="resourceLocation">The type's identifier.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ConfiguredFeaturePropertyAttribute(string resourceLocation) : Attribute
{
    /// <summary>
    /// Gets the type's identifier.
    /// </summary>
    public string ResourceLocation { get; init; } = resourceLocation;
}
