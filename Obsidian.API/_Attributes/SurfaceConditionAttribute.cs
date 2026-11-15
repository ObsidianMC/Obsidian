namespace Obsidian.API;

/// <summary>
/// Marks a class as the implementation of a surface rule condition type that worldgen data files name by their
/// <c>type</c>.
/// </summary>
/// <param name="identifier">The condition type's identifier.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SurfaceConditionAttribute(string identifier) : Attribute
{
    /// <summary>
    /// Gets the condition type's identifier.
    /// </summary>
    public string Identifier { get; } = identifier;
}
