namespace Obsidian.API;

/// <summary>
/// Marks a class as the worldgen type that data files name by <see cref="ResourceLocation"/>.
/// </summary>
/// <param name="resourceLocation">The type's identifier.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ConfiguredFeatureAttribute(string resourceLocation) : Attribute
{
    /// <summary>
    /// Gets the type's identifier.
    /// </summary>
    public string ResourceLocation { get; } = resourceLocation;
}
