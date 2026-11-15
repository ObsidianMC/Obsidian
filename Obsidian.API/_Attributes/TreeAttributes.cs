namespace Obsidian.API;

/// <summary>
/// Marks a class as the implementation of a type used in tree features that worldgen data files name by
/// <see cref="ResourceLocation"/>. A class can carry several.
/// </summary>
/// <param name="resourceLocation">The type's identifier.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class TreePropertyAttribute(string resourceLocation) : Attribute
{
    /// <summary>
    /// Gets the type's identifier.
    /// </summary>
    public string ResourceLocation { get; init; } = resourceLocation;
}
