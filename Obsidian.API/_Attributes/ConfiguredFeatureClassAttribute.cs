namespace Obsidian.API;

/// <summary>
/// Marks a class as the implementation of a feature type, like <c>minecraft:bamboo</c>, that configured features in
/// data files name by their <c>type</c>.
/// </summary>
/// <param name="identifier">The feature type's identifier.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ConfiguredFeatureClassAttribute(string identifier) : Attribute
{
    /// <summary>
    /// Gets the feature type's identifier.
    /// </summary>
    public string Identifier { get; } = identifier;
}
