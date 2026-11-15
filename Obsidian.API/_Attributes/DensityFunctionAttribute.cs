namespace Obsidian.API;

/// <summary>
/// Marks a class as the implementation of a density function type that worldgen data files name by their
/// <c>type</c>.
/// </summary>
/// <param name="type">The density function type's identifier.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class DensityFunctionAttribute(string type) : Attribute
{
    /// <summary>
    /// Gets the density function type's identifier.
    /// </summary>
    public string Type { get; } = type;
}
